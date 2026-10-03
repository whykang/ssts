using System.Text.Json.Nodes;
using AngleSharp.Dom;

namespace TingShu.Sdk;

/// <summary>
/// 音频地址提取策略：把 <see cref="Episode.Url"/> 转成可以直接播放的音频地址。
/// </summary>
public abstract class AudioExtractor
{
    /// <summary>章节地址已经是音频地址</summary>
    public static AudioExtractor Direct { get; } = new DirectExtractor();

    /// <summary>完全自定义：传入章节地址，返回音频地址。可在里面调用 Host 的任何能力（包括 WebView）。</summary>
    public static AudioExtractor Custom(Func<string, CancellationToken, Task<string>> extract) => new CustomExtractor(extract);

    /// <summary>请求章节网页，用 HTML 文档解析出音频地址</summary>
    public static AudioExtractor Html(Func<IDocument, string?> parse, bool desktop = false) => new HtmlExtractor(parse, desktop);

    /// <summary>请求章节地址（返回 JSON），解析出音频地址</summary>
    public static AudioExtractor Json(Func<JsonNode, string?> parse, bool desktop = false) => new JsonExtractor(parse, desktop);

    /// <summary>
    /// 用 WebView 渲染页面后执行 <paramref name="script"/>（默认返回整个 HTML），
    /// 把脚本结果交给 <paramref name="parse"/>，返回 null 或空字符串时会继续重试直到超时。
    /// </summary>
    public static AudioExtractor WebView(Func<string, string?> parse, bool desktop = false, string? script = null)
        => new WebViewExtractor(parse, desktop, script);

    /// <summary>
    /// 用 WebView 打开页面，自动嗅探页面发出的音频请求。
    /// <paramref name="script"/> 为可选的触发脚本（例如调用页面自己的播放函数），页面加载后会定时执行直到嗅探到音频。
    /// </summary>
    public static AudioExtractor WebViewSniff(Func<string, bool>? validate = null, bool desktop = false, string? script = null)
        => new WebViewSniffExtractor(validate, desktop, script);

    public sealed class DirectExtractor : AudioExtractor { }

    public sealed class CustomExtractor(Func<string, CancellationToken, Task<string>> extract) : AudioExtractor
    {
        public Func<string, CancellationToken, Task<string>> Extract { get; } = extract;
    }

    public sealed class HtmlExtractor(Func<IDocument, string?> parse, bool desktop) : AudioExtractor
    {
        public Func<IDocument, string?> Parse { get; } = parse;
        public bool Desktop { get; } = desktop;
    }

    public sealed class JsonExtractor(Func<JsonNode, string?> parse, bool desktop) : AudioExtractor
    {
        public Func<JsonNode, string?> Parse { get; } = parse;
        public bool Desktop { get; } = desktop;
    }

    public sealed class WebViewExtractor(Func<string, string?> parse, bool desktop, string? script) : AudioExtractor
    {
        public Func<string, string?> Parse { get; } = parse;
        public bool Desktop { get; } = desktop;
        public string? Script { get; } = script;
    }

    public sealed class WebViewSniffExtractor(Func<string, bool>? validate, bool desktop, string? script = null) : AudioExtractor
    {
        public Func<string, bool>? Validate { get; } = validate;
        public bool Desktop { get; } = desktop;
        public string? Script { get; } = script;
    }
}
