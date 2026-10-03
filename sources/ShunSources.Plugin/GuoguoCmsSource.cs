using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 念音网、有听网使用的同一套网站程序（?s=ting-search-wd-… 搜索，音频存放在 guoguo.org.cn）
/// </summary>
public abstract class GuoguoCmsSource : ShunSource, IAudioHeaders
{
    protected abstract string Site { get; }

    public override string Url => Site;
    protected override string CoverDomain => "guoguo.org";

    private Task<IDocument> Get(string url, CancellationToken ct) => GetDoc(url, true, ct, referer: Site);

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await Get($"{Site}?s=ting-search-wd-{Enc(keywords)}-p-{page}.html", ct);
        var books = ParseBooks(doc, withStatus: false);
        var current = RegexInt(doc.SelectFirst(".category-list > .c-page > .current").Text(), @"(\d+)");
        var last = doc.SelectFirst(".category-list > .c-page") is { } p ? FindByText(p, "a", "尾页") : null;
        return new SearchResult(books, RegexInt(last.AbsUrl("href"), @"p-(\d+)\.html", current));
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc(Site, true, ct);
        var tabs = doc.SelectFirst(".nav")?.Select("a")
            .Where(a => a.Text() != "首页")
            .Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList() ?? new List<CategoryTab>();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await Get(url, ct);
        var current = RegexInt(doc.SelectFirst(".category-list > .c-page > .current").Text(), @"(\d+)");
        var pager = doc.SelectFirst(".category-list > .c-page");
        var total = RegexInt(pager == null ? null : FindByText(pager, "a", "尾页").AbsUrl("href"), @"index(\d+)\.html", current);
        var next = pager == null ? null : FindByText(pager, "a", "下一页")?.AbsUrl("href");
        return new CategoryPage(ParseBooks(doc, withStatus: true), current, Math.Max(total, current), url, next);
    }

    private static List<Book> ParseBooks(IDocument doc, bool withStatus)
    {
        var books = new List<Book>();
        foreach (var li in doc.Select(".category-list > ul li"))
        {
            var a = li.SelectFirst(".info > h4 > a");
            if (a == null) continue;
            var p = li.SelectFirst(".info")?.Select("p");
            books.Add(new Book(li.SelectFirst(".img > a > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                StripLabel(p?.ElementAtOrDefault(1).Text()), StripLabel(p?.ElementAtOrDefault(2).Text()))
            {
                Status = withStatus ? StripLabel(p?.ElementAtOrDefault(3).Text()) : "",
            });
        }
        return books;
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await Get(bookUrl, ct);
        var list = loadEpisodes
            ? doc.Select(".plist > ul li a").Select(a => new Episode(a.Text(), a.AbsUrl("href"))).ToList()
            : new List<Episode>();
        return new BookDetail(list, doc.SelectFirst(".intro > p").Text());
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.WebView(html =>
    {
        var src = Host.ParseHtml(html, Site).SelectFirst("#jp_audio_0").AbsUrl("src");
        return src.Replace("https://cloud.guoguo.org.cn/nyts.php?uid=", "https://oss-links.guoguo.org.cn/uploads/");
    }, desktop: true);

    public IDictionary<string, string>? GetAudioHeaders(string audioUrl) =>
        audioUrl.Contains("guoguo") ? new Dictionary<string, string> { ["Referer"] = Site } : null;
}

/// <summary>念音网 nianyin.com</summary>
public sealed class NiantingWang : GuoguoCmsSource
{
    public override string Id => "d87f1316fa2e41a299074e2b0c086a17";
    public override string Name => "念音网";
    protected override string Site => "https://www.nianyin.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n不是所有都能听，有的可能要会员。";
}

/// <summary>有听网 ting15.com</summary>
public sealed class YoutingWang : GuoguoCmsSource
{
    public override string Id => "c3c5bdc9145c4200a38e9a3558861e98";
    public override string Name => "有听网";
    protected override string Site => "https://www.ting15.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n不是所有都能听，有的可能要会员。";
}
