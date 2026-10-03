using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>中文听书网 tingzh.com</summary>
public sealed class TingZg : ShunSource, ISearchVerification
{
    public override string Id => "17251780a2804f2fae21f93d3efd2a66";
    public override string Name => "中文听书网";
    public override string Url => "https://www.tingzh.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n【已失效】网站目前无法连接，源保留以便网站恢复。搜索可能需要验证。";
    public override bool EnabledByDefault => false;
    protected override string CoverDomain => "tingzh.com";

    public string GetSearchVerificationUrl(string keywords) => $"https://www.tingzh.com/search.php?searchword={Enc(keywords)}";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"https://www.tingzh.com/search.php?page={page}&searchword={Enc(keywords)}&searchtype=", true, ct,
            withCookies: true, referer: Url);
        var books = new List<Book>();
        foreach (var li in doc.Select(".clist.sear_height > ul li"))
        {
            var p = li.Select("p");
            if (p.Length < 5) continue;
            books.Add(new Book(li.SelectFirst("a > img").AbsUrl("src"), li.SelectFirst("a").AbsUrl("href"), p[0].SelectFirst("b > a").Text(),
                p[1].Text(), p[3].SelectFirst("a").Text())
            {
                Intro = p[2].Text(),
                Status = p[4].Text(),
            });
        }
        var total = RegexInt(doc.Select(".clist.sear_height > .page > span").Select(e => e.Text()).FirstOrDefault(), @"页次:\d+/(\d+)页", page);
        return new SearchResult(books, total);
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc(Url, true, ct);
        var tabs = doc.Select("#nav ul > li > a")
            .Where(a => a.Text() is not ("首 页" or "首页"))
            .Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct, withCookies: true, referer: Url);
        var pager = string.Join(" ", doc.Select(".clist > .page > span > span").Select(e => e.Text()));
        var current = RegexInt(pager, @"页次:(\d+)/");
        var total = RegexInt(pager, @"页次:\d+/(\d+)页", current);
        var next = doc.SelectFirst(".clist > .page span") is { } span ? FindByText(span, "a", "下一页")?.AbsUrl("href") : null;
        var books = new List<Book>();
        foreach (var li in doc.Select(".clist > ul li"))
        {
            var p = li.Select("p");
            if (p.Length < 5) continue;
            var a = li.SelectFirst("a");
            books.Add(new Book(li.SelectFirst("a > img").AbsUrl("src"), a.AbsUrl("href"), a.Attr("title"), p[1].Text(), p[3].SelectFirst("a").Text())
            {
                Intro = p[2].Text(),
                Status = p[4].Text(),
            });
        }
        return new CategoryPage(books, current, total, url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await GetDoc(bookUrl, true, ct);
        var list = loadEpisodes
            ? doc.Select(".compress li a").Select(a => new Episode(a.Attr("title") is { Length: > 0 } t ? t : a.Text(), a.AbsUrl("href"))).ToList()
            : new List<Episode>();
        return new BookDetail(list, doc.SelectFirst(".introBox").Text());
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.WebViewSniff(desktop: true);
}
