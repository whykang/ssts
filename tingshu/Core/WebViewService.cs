using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace tingshu.Core;

/// <summary>
/// 后台（不可见）WebView2：用于渲染 JS 页面提取音频、嗅探音频请求、读取登录 Cookie。
/// 所有 WebView 操作都切换到 UI 线程，并串行执行。
/// </summary>
public sealed class WebViewService
{
    public static WebViewService Instance { get; } = new();

    private const string DefaultScript = "(function(){return '<html>'+document.documentElement.innerHTML+'</html>';})();";
    private static readonly string[] MediaExtensions = { ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wav", ".wma", ".ape", ".m3u8", ".mp4", ".m4b" };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<CoreWebView2Environment>? _envTask;
    private CoreWebView2Controller? _controller;

    public bool? IsAvailable { get; private set; }

    public static string? RuntimeVersion
    {
        get
        {
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch { return null; }
        }
    }

    public Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        return _envTask ??= CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir,
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
    }

    private static Task<T> OnUi<T>(Func<Task<T>> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return Task.FromException<T>(new InvalidOperationException("WebView 不可用：程序界面尚未启动"));
        return dispatcher.CheckAccess() ? func() : dispatcher.InvokeAsync(func).Task.Unwrap();
    }

    private async Task<CoreWebView2> GetWebViewAsync()
    {
        if (_controller != null) return _controller.CoreWebView2;
        if (RuntimeVersion == null)
        {
            IsAvailable = false;
            throw new InvalidOperationException("未检测到 WebView2 运行时，请安装 Microsoft Edge WebView2 Runtime 后重试。");
        }
        var env = await GetEnvironmentAsync();
        var hwnd = new WindowInteropHelper(Application.Current.MainWindow!).Handle;
        _controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        _controller.IsVisible = false;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, 1280, 800);
        var core = _controller.CoreWebView2;
        core.IsMuted = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        IsAvailable = true;
        return core;
    }

    /// <summary>加载页面并反复执行脚本，直到 parse 返回非空结果</summary>
    public Task<string> RenderAsync(string url, bool desktop, string? script, Func<string, string?>? parse,
        TimeSpan timeout, CancellationToken ct) => OnUi(async () =>
    {
        await _gate.WaitAsync(ct);
        try
        {
            var core = await GetWebViewAsync();
            core.Settings.UserAgent = HttpService.UserAgent(desktop);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            var loaded = new TaskCompletionSource();
            void OnDom(object? s, CoreWebView2DOMContentLoadedEventArgs e) => loaded.TrySetResult();
            core.DOMContentLoaded += OnDom;
            try
            {
                core.Navigate(url);
                await loaded.Task.WaitAsync(timeoutCts.Token);
                string? last = null;
                while (true)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    var raw = await core.ExecuteScriptAsync(script ?? DefaultScript);
                    var value = DecodeScriptResult(raw);
                    last = value;
                    var parsed = parse == null ? value : SafeParse(parse, value);
                    if (!string.IsNullOrWhiteSpace(parsed)) return parsed;
                    await Task.Delay(500, timeoutCts.Token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"WebView 加载超时：{url}");
            }
            finally
            {
                core.DOMContentLoaded -= OnDom;
                core.Navigate("about:blank");
            }
        }
        finally
        {
            _gate.Release();
        }
    });

    /// <summary>打开页面，嗅探第一个音频请求</summary>
    public Task<string> SniffAsync(string url, bool desktop, Func<string, bool>? validate, TimeSpan timeout, CancellationToken ct,
        string? trigger = null) => OnUi(async () =>
    {
        await _gate.WaitAsync(ct);
        try
        {
            var core = await GetWebViewAsync();
            core.Settings.UserAgent = HttpService.UserAgent(desktop);
            var found = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            bool Accept(string u)
            {
                if (string.IsNullOrEmpty(u) || u.StartsWith("blob:") || u.StartsWith("data:")) return false;
                if (validate != null) return SafeValidate(validate, u);
                var path = u.Split('?')[0].ToLowerInvariant();
                return MediaExtensions.Any(path.EndsWith);
            }

            void OnRequest(object? s, CoreWebView2WebResourceRequestedEventArgs e)
            {
                var u = e.Request.Uri;
                if (Accept(u) || (validate == null && e.ResourceContext == CoreWebView2WebResourceContext.Media && !u.StartsWith("blob:")))
                    found.TrySetResult(u);
            }

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnRequest;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                core.Navigate(url);
                // 定时尝试触发播放，并检查 audio/video 元素的 src
                while (!found.Task.IsCompleted)
                {
                    // WhenAny 不会因取消而抛异常，必须自己检查超时，否则会一直循环并占住 WebView
                    await Task.WhenAny(found.Task, Task.Delay(800, timeoutCts.Token));
                    if (found.Task.IsCompleted) break;
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    // 插件提供的触发脚本（例如调用页面自己的播放函数）
                    if (!string.IsNullOrEmpty(trigger))
                    {
                        try { await core.ExecuteScriptAsync(trigger); } catch { }
                    }
                    // 尝试播放媒体元素；很多播放器（jPlayer / APlayer 等）要点击播放按钮才会请求音频
                    var raw = await core.ExecuteScriptAsync(
                        "(function(){if(!window.__tsClicked){window.__tsClicked=1;" +
                        "var b=document.querySelector('.jp-play,.aplayer-play,.aplayer-button,.vjs-play-control,#play,.play-btn,.btn-play,.playbtn');" +
                        "if(b){try{b.click();}catch(e){}}}" +
                        "var m=document.querySelector('audio,video');if(!m)return '';try{m.muted=true;m.play();}catch(e){}return m.currentSrc||m.src||'';})();");
                    var src = DecodeScriptResult(raw);
                    if (Accept(src) || (validate == null && src.StartsWith("http"))) found.TrySetResult(src);
                }
                return await found.Task;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("没有嗅探到音频地址（可能需要登录或网站已失效）");
            }
            finally
            {
                core.WebResourceRequested -= OnRequest;
                core.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.Navigate("about:blank");
            }
        }
        finally
        {
            _gate.Release();
        }
    });

    public async Task<string> GetCookiesAsync(string url)
    {
        var cookies = await GetCookieListAsync(url);
        return string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
    }

    /// <summary>读取 WebView 中某个地址可用的 Cookie（含域名和路径）</summary>
    public async Task<IReadOnlyList<System.Net.Cookie>> GetCookieListAsync(string url)
    {
        try
        {
            return await OnUi(async () =>
            {
                var core = await GetWebViewAsync();
                var cookies = await core.CookieManager.GetCookiesAsync(url);
                return (IReadOnlyList<System.Net.Cookie>)cookies.Select(c => c.ToSystemNetCookie()).ToList();
            });
        }
        catch (Exception ex)
        {
            Logger.Error("读取 WebView Cookie 失败", ex);
            return Array.Empty<System.Net.Cookie>();
        }
    }

    private static string DecodeScriptResult(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "null") return "";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() ?? "" : doc.RootElement.GetRawText();
        }
        catch
        {
            return raw;
        }
    }

    private static string? SafeParse(Func<string, string?> parse, string value)
    {
        try { return parse(value); }
        catch (Exception ex)
        {
            Logger.Error("WebView 结果解析失败", ex);
            return null;
        }
    }

    private static bool SafeValidate(Func<string, bool> validate, string url)
    {
        try { return validate(url); }
        catch { return false; }
    }
}
