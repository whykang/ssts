using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>恋听网 m.ting55.com</summary>
public sealed class LianTingWang : ShunSource, ICertificateNameMismatchHosts, IAudioHeaders
{
    public IDictionary<string, string>? GetAudioHeaders(string audioUrl) =>
        audioUrl.Contains("ting55.com") ? new Dictionary<string, string> { ["Referer"] = "https://m.ting55.com/" } : null;

    public override string Id => "5caf7568d5f64406822e3a41364a016c";
    public override string Name => "恋听网";
    public override string Url => "https://m.ting55.com";
    public override string Description => "推荐指数:5星 ⭐⭐⭐⭐⭐\n部分书籍是收费内容，不支持播放。";
    public override bool EnabledByDefault => false;

    // 音频服务器 pp.ting55.com 的证书是 *.aliyun.com（网站 CDN 配置问题）
    public IReadOnlyCollection<string> NameMismatchHosts => new[] { "pp.ting55.com" };

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"https://m.ting55.com/search/{Enc(keywords)}/page/{page}", false, ct);
        var (_, total) = ParsePager(doc);
        return new SearchResult(ParseBooks(doc.Select(".slist > a")), total);
    }

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        static CategoryTab T(string t, string path) => new(t, "https://m.ting55.com/" + path);
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("有声小说", new[]
            {
                T("推荐", "tuijian"), T("玄幻", "category/1"), T("武侠", "category/2"), T("都市", "category/3"), T("言情", "category/4"),
                T("穿越", "category/5"), T("科幻", "category/6"), T("推理", "category/7"), T("恐怖", "category/8"), T("惊悚", "category/9"),
            }),
            new CategoryMenu("其它", new[]
            {
                T("历史", "category/10"), T("经典", "category/11"), T("相声", "category/12"), T("评书", "category/14"), T("百家讲坛", "category/13"),
            }),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, false, ct);
        var (current, total) = ParsePager(doc);
        var next = doc.SelectFirst(".cpage") is { } cp ? cp.Select("a").FirstOrDefault(a => a.Text() == "下一页")?.AbsUrl("href") : null;
        return new CategoryPage(ParseBooks(doc.Select(".clist > a")), current, total, url, next);
    }

    /// <summary>“页次 1/20”</summary>
    private static (int Current, int Total) ParsePager(IDocument doc)
    {
        var text = doc.SelectFirst(".cpage span").Text().Replace("页次", "").Trim();
        var parts = text.Split('/');
        return parts.Length == 2 && int.TryParse(parts[0], out var c) && int.TryParse(parts[1], out var t) ? (c, t) : (1, 1);
    }

    private static List<Book> ParseBooks(IEnumerable<IElement> items)
    {
        var books = new List<Book>();
        foreach (var a in items)
        {
            var infos = a.SelectFirst("dl > dd")?.Children;
            if (infos == null || infos.Length < 3) continue;
            books.Add(new Book(a.SelectFirst("dl > dt > img").AbsUrl("src"), a.AbsUrl("href"), infos[0].Text(), StripLabel(infos[1].Text()), StripLabel(infos[2].Text()))
            {
                Status = infos.Length > 3 ? StripLabel(infos[3].Text()) : "",
            });
        }
        return books;
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await GetDoc(bookUrl, false, ct);
        var episodes = doc.Select(".plist > a")
            .Select(a => new Episode(a.Text(), a.AbsUrl("href")) { IsFree = a.ClassList.Contains("f") }).ToList();
        return new BookDetail(episodes, doc.SelectFirst(".intro").Text());
    }

    public override AudioExtractor GetAudioExtractor() =>
        AudioExtractor.WebView(html => Host.ParseHtml(html).SelectFirst("audio")?.GetAttribute("src"));
}
