using System.Text.Json.Nodes;
using AngleSharp.Dom;

namespace TingShu.Sdk;

/// <summary>
/// 网络请求选项
/// </summary>
public sealed class RequestOptions
{
    /// <summary>true 使用电脑版 UA，false 使用手机版 UA</summary>
    public bool Desktop { get; set; }
    /// <summary>GET / POST / ...</summary>
    public string Method { get; set; } = "GET";
    /// <summary>请求体（POST 时使用）</summary>
    public string? Body { get; set; }
    /// <summary>请求体类型，默认 application/x-www-form-urlencoded</summary>
    public string? ContentType { get; set; }
    /// <summary>额外请求头</summary>
    public Dictionary<string, string>? Headers { get; set; }
    /// <summary>强制使用的编码（例如 "gbk"）。为空时自动从响应头或 meta 判断</summary>
    public string? Encoding { get; set; }
    /// <summary>是否附带 WebView 中的 Cookie（登录后的状态）</summary>
    public bool UseWebViewCookies { get; set; }

    public static RequestOptions Pc => new() { Desktop = true };
    public static RequestOptions Mobile => new() { Desktop = false };
}

/// <summary>
/// 宿主（App）提供给插件的能力。每个源拥有独立的 Host 实例。
/// </summary>
public interface ISourceHost
{
    /// <summary>当前源 ID</summary>
    string SourceId { get; }

    string DesktopUserAgent { get; }
    string MobileUserAgent { get; }

    /// <summary>共享的 HttpClient（自动解压、共享 Cookie、跟随系统代理）</summary>
    HttpClient Http { get; }

    /// <summary>请求文本，自动识别 GBK / UTF-8 等编码</summary>
    Task<string> GetStringAsync(string url, RequestOptions? options = null, CancellationToken ct = default);

    /// <summary>请求并解析 HTML。返回的文档已设置 BaseUrl，可用 <c>element.AbsUrl("href")</c> 获取绝对地址</summary>
    Task<IDocument> GetHtmlAsync(string url, RequestOptions? options = null, CancellationToken ct = default);

    /// <summary>请求并解析 JSON</summary>
    Task<JsonNode> GetJsonAsync(string url, RequestOptions? options = null, CancellationToken ct = default);

    /// <summary>解析一段 HTML</summary>
    IDocument ParseHtml(string html, string? baseUrl = null);

    /// <summary>
    /// 用 WebView 加载页面，等待 <paramref name="script"/> 返回非空结果（默认返回整个 HTML）。
    /// 适合需要执行 JS 才能拿到内容、或者有 Cloudflare 等验证的网站。
    /// </summary>
    Task<string> RenderAsync(string url, bool desktop = false, string? script = null, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>用 WebView 打开页面并嗅探音频地址。script 为可选的触发播放脚本</summary>
    Task<string> SniffMediaAsync(string url, bool desktop = false, Func<string, bool>? validate = null, TimeSpan? timeout = null,
        CancellationToken ct = default, string? script = null);

    /// <summary>读取 WebView 中保存的 Cookie（例如登录后），格式 a=1; b=2</summary>
    Task<string> GetWebViewCookiesAsync(string url);

    /// <summary>读取源自己的配置（包括 <see cref="IConfigurableSource"/> 中定义的项）</summary>
    string? GetPref(string key, string? defaultValue = null);

    /// <summary>保存源自己的配置</summary>
    void SetPref(string key, string? value);

    /// <summary>界面提示</summary>
    void Toast(string message);

    /// <summary>调试日志，会显示在插件页面的日志中</summary>
    void Log(string message);

    /// <summary>加载多页章节时报告进度，传 null 表示加载完毕</summary>
    void ReportProgress(string? info);

    /// <summary>源独立的缓存目录</summary>
    string GetCacheDir(string subDir = "");
}
