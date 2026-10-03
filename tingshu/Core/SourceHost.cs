using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TingShu.Sdk;

namespace tingshu.Core;

/// <summary>
/// 提供给每个源的宿主能力
/// </summary>
public sealed class SourceHost(string sourceId, string sourceName) : ISourceHost
{
    private static readonly HtmlParser Parser = new(new HtmlParserOptions { IsScripting = false });

    /// <summary>加载章节时的进度通知（UI 订阅）</summary>
    public static event Action<string, string?>? ProgressReported;
    public static event Action<string>? ToastRequested;

    public string SourceId { get; } = sourceId;
    public string DesktopUserAgent => AppSettings.Current.DesktopUA;
    public string MobileUserAgent => AppSettings.Current.MobileUA;
    public HttpClient Http => HttpService.Client;

    public Task<string> GetStringAsync(string url, RequestOptions? options = null, CancellationToken ct = default)
        => HttpService.GetStringAsync(url, options, ct);

    public async Task<IDocument> GetHtmlAsync(string url, RequestOptions? options = null, CancellationToken ct = default)
    {
        var html = await HttpService.GetStringAsync(url, options, ct);
        return ParseHtml(html, url);
    }

    public async Task<JsonNode> GetJsonAsync(string url, RequestOptions? options = null, CancellationToken ct = default)
    {
        var text = await HttpService.GetStringAsync(url, options, ct);
        return ParseJson(text, url);
    }

    public static JsonNode ParseJson(string text, string url)
    {
        text = text.Trim();
        // 兼容 JSONP：callback({...})
        if (text.Length > 0 && text[0] != '{' && text[0] != '[')
        {
            var l = text.IndexOfAny(new[] { '{', '[' });
            var r = text.LastIndexOfAny(new[] { '}', ']' });
            if (l >= 0 && r > l) text = text[l..(r + 1)];
        }
        return JsonNode.Parse(text) ?? throw new InvalidDataException($"返回内容不是 JSON：{url}");
    }

    public IDocument ParseHtml(string html, string? baseUrl = null) => ParseHtmlStatic(html, baseUrl);

    public static IDocument ParseHtmlStatic(string html, string? baseUrl)
    {
        var doc = Parser.ParseDocument(html);
        if (!string.IsNullOrEmpty(baseUrl) && doc.Head != null && doc.QuerySelector("base[href]") == null)
        {
            var b = doc.CreateElement("base");
            b.SetAttribute("href", baseUrl);
            doc.Head.Prepend(b);
        }
        return doc;
    }

    public Task<string> RenderAsync(string url, bool desktop = false, string? script = null, TimeSpan? timeout = null, CancellationToken ct = default)
        => WebViewService.Instance.RenderAsync(url, desktop, script, null, timeout ?? TimeSpan.FromSeconds(25), ct);

    public Task<string> SniffMediaAsync(string url, bool desktop = false, Func<string, bool>? validate = null, TimeSpan? timeout = null,
        CancellationToken ct = default, string? script = null)
        => WebViewService.Instance.SniffAsync(url, desktop, validate, timeout ?? TimeSpan.FromSeconds(25), ct, script);

    public Task<string> GetWebViewCookiesAsync(string url) => WebViewService.Instance.GetCookiesAsync(url);

    public string? GetPref(string key, string? defaultValue = null)
        => AppSettings.Current.SourcePrefs.TryGetValue($"{SourceId}.{key}", out var v) ? v : defaultValue;

    public void SetPref(string key, string? value)
    {
        var k = $"{SourceId}.{key}";
        if (value == null) AppSettings.Current.SourcePrefs.Remove(k);
        else AppSettings.Current.SourcePrefs[k] = value;
        AppSettings.Current.Save();
    }

    public void Toast(string message) => ToastRequested?.Invoke(message);

    public void Log(string message) => Logger.Info($"[{sourceName}] {message}");

    public void ReportProgress(string? info) => ProgressReported?.Invoke(SourceId, info);

    public string GetCacheDir(string subDir = "")
        => AppPaths.Ensure(Path.Combine(AppPaths.CacheDir, "sources", SourceId, subDir));
}
