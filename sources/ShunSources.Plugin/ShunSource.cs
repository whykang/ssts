using System.Text.RegularExpressions;
using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 本插件包中各源共用的辅助方法
/// </summary>
public abstract class ShunSource : SourceBase, ICoverHeaders
{
    /// <summary>封面地址包含这个字符串时，加上 Referer（防盗链）。为空表示不处理</summary>
    protected virtual string? CoverDomain => null;

    /// <summary>防盗链使用的 Referer，默认是站点首页</summary>
    protected virtual string Referer => Url;

    public bool TryGetCoverHeaders(string coverUrl, IDictionary<string, string> headers)
    {
        if (CoverDomain == null || !coverUrl.Contains(CoverDomain)) return false;
        headers["Referer"] = Referer;
        return true;
    }

    /// <summary>请求网页</summary>
    protected Task<IDocument> GetDoc(string url, bool desktop, CancellationToken ct, bool withCookies = false, string? referer = null,
        string? encoding = null)
    {
        var options = new RequestOptions { Desktop = desktop, UseWebViewCookies = withCookies, Encoding = encoding };
        if (referer != null) options.Headers = new Dictionary<string, string> { ["Referer"] = referer };
        return Host.GetHtmlAsync(url, options, ct);
    }

    /// <summary>在 selector 选中的元素中找文字包含 text 的第一个</summary>
    protected static IElement? FindByText(IParentNode node, string selector, string text) =>
        node.QuerySelectorAll(selector).FirstOrDefault(e => e.Text().Contains(text));

    protected static int RegexInt(string? input, string pattern, int defaultValue = 1)
    {
        var m = Regex.Match(input ?? "", pattern);
        return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : defaultValue;
    }

    protected static string Enc(string keywords) => Uri.EscapeDataString(keywords);

    /// <summary>去掉“作者：”“播音：”这类标签前缀</summary>
    protected static string StripLabel(string? text) =>
        Regex.Replace(text ?? "", @"^\s*[\u4e00-\u9fa5A-Za-z]{1,4}\s*[：:]\s*", "").Trim();

    /// <summary>
    /// 这类站点的播放页里用 var now="音频地址" 保存当前音频；取不到时回退为 WebView 嗅探
    /// </summary>
    protected AudioExtractor VarNowExtractor(bool desktop) => AudioExtractor.Custom(async (url, ct) =>
    {
        var html = await Host.GetStringAsync(url, new RequestOptions { Desktop = desktop, UseWebViewCookies = true }, ct);
        var m = Regex.Match(html, @"var\s+now\s*=\s*[""']([^""']+)[""']");
        var audio = m.Success ? Regex.Unescape(m.Groups[1].Value) : "";
        if (audio.StartsWith("http")) return audio;
        return await Host.SniffMediaAsync(url, desktop, ContainsAudioExt, ct: ct);
    });

    /// <summary>手动跟随跳转，得到最终地址（HttpClient 不会自动从 https 跳到 http）</summary>
    protected async Task<string> ResolveRedirects(string url, CancellationToken ct)
    {
        for (var i = 0; i < 6; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", Host.MobileUserAgent);
            using var resp = await Host.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)resp.StatusCode;
            if (code < 300 || code >= 400 || resp.Headers.Location == null) return resp.RequestMessage?.RequestUri?.ToString() ?? url;
            url = new Uri(new Uri(url), resp.Headers.Location).ToString();
        }
        return url;
    }

    private static readonly string[] AudioExts = { ".m4a", ".mp3", ".m4b", ".flac", ".aa3", ".ogg", ".wma", ".wav", ".aac", ".ac3", ".mp4" };

    /// <summary>原作者嗅探时使用的判断：地址中包含常见音频扩展名</summary>
    protected static bool ContainsAudioExt(string url) => AudioExts.Any(e => url.Contains(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 安卓版音频嗅探的默认判断：常见音频扩展名
    /// </summary>
    protected static bool IsAudio(string url)
    {
        var path = url.Split('?')[0].ToLowerInvariant();
        return path.EndsWith(".mp3") || path.EndsWith(".m4a") || path.EndsWith(".aac") || path.EndsWith(".flac")
               || path.EndsWith(".wav") || path.EndsWith(".ogg") || path.EndsWith(".m3u8") || path.EndsWith(".mp4");
    }
}
