using TingShu.Sdk;

namespace tingshu.Core;

/// <summary>
/// 根据源的 <see cref="AudioExtractor"/> 把章节地址解析为音频地址
/// </summary>
public static class AudioResolver
{
    private static readonly TimeSpan WebViewTimeout = TimeSpan.FromSeconds(30);

    public static async Task<(string Url, IDictionary<string, string>? Headers)> ResolveAsync(SourceEntry entry, Episode episode, CancellationToken ct)
    {
        var source = entry.Source;
        var extractor = source.GetAudioExtractor();
        var host = new SourceHost(source.Id, source.Name);
        string? url = extractor switch
        {
            AudioExtractor.DirectExtractor => episode.Url,
            AudioExtractor.CustomExtractor c => await Task.Run(() => c.Extract(episode.Url, ct), ct),
            AudioExtractor.HtmlExtractor h => await Task.Run(async () =>
                h.Parse(await host.GetHtmlAsync(episode.Url, new RequestOptions { Desktop = h.Desktop }, ct)), ct),
            AudioExtractor.JsonExtractor j => await Task.Run(async () =>
                j.Parse(await host.GetJsonAsync(episode.Url, new RequestOptions { Desktop = j.Desktop }, ct)), ct),
            AudioExtractor.WebViewExtractor w => await WebViewService.Instance.RenderAsync(episode.Url, w.Desktop, w.Script, w.Parse, WebViewTimeout, ct),
            AudioExtractor.WebViewSniffExtractor s => await WebViewService.Instance.SniffAsync(episode.Url, s.Desktop, s.Validate, WebViewTimeout, ct, s.Script),
            _ => throw new NotSupportedException($"不支持的提取方式：{extractor.GetType().Name}"),
        };

        url = url?.Trim();
        if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("没有解析到音频地址");
        if (url.StartsWith("//")) url = "https:" + url;
        // 在后台线程调用：插件可能在里面同步读取 WebView Cookie（需要界面线程空闲）
        var audioUrl = url;
        var headers = source is IAudioHeaders ah ? await Task.Run(() => ah.GetAudioHeaders(audioUrl), ct) : null;
        Logger.Info($"[{source.Name}] {episode.Title} → {url}");
        return (url, headers);
    }
}
