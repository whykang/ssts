using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 乐听吧、全民听书网使用的同一套网站程序（.row3.row-b 列表 + .pagebar 分页）
/// </summary>
public abstract class PageBarCmsSource : ShunSource, ISearchVerification
{
    /// <summary>站点根地址，如 https://www.leting8.com/</summary>
    protected abstract string Site { get; }

    public override string Url => Site;
    protected override string CoverDomain => new Uri(Site).Host;

    public string GetSearchVerificationUrl(string keywords) => $"{Site}search.php?searchword={Enc(keywords)}";

    // 网站限制两次搜索的间隔（原版同样延迟 6 秒）
    public int SearchDelaySeconds => 6;

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"{Site}search.php?page={page}&searchword={Enc(keywords)}&searchtype=", true, ct, withCookies: true);
        var books = ParseBooks(doc.SelectFirst(".row3.row-b")?.Select("li"));
        var last = doc.SelectFirst(".pagebar.ta-c.mb15 > span") is { } bar ? FindByText(bar, "a", "尾页") : null;
        return new SearchResult(books, RegexInt(last.AbsUrl("href"), @"page=(\d+)", page));
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc(Site, true, ct);
        var tabs = doc.Select(".nav.mb10 > ul li > a")
            .Where(a => a.Text() != "首页")
            .Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var current = RegexInt(doc.SelectFirst(".pagebar.ta-c.mb15 > span > .page.now-page").Text(), @"(\d+)");
        var bar = doc.SelectFirst(".pagebar.ta-c.mb15 > span");
        var total = RegexInt(bar == null ? null : FindByText(bar, "a", "尾页").AbsUrl("href"), @"-(\d+)", current);
        var next = bar == null ? null : FindByText(bar, "a", "››")?.AbsUrl("href");
        return new CategoryPage(ParseBooks(doc.Select(".row3.row-b > li")), current, Math.Max(total, current), url, next);
    }

    private static List<Book> ParseBooks(IEnumerable<IElement>? items)
    {
        var books = new List<Book>();
        foreach (var li in items ?? Array.Empty<IElement>())
        {
            var a = li.SelectFirst(".style-img.clearfix > section > h2 > a");
            if (a == null) continue;
            books.Add(new Book(li.SelectFirst(".style-img.clearfix > .img-80.fl.mr15 > span > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                li.SelectFirst(".style-img.clearfix > section > h2 > span").Text())
            {
                Intro = li.SelectFirst(".style-img.clearfix > section > .f-gray.mb10.f-12").Text(),
            });
        }
        return books;
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await GetDoc(bookUrl, true, ct);
        var list = loadEpisodes
            ? doc.Select(".ul-36.clearfix li a").Select(a => new Episode(a.Text(), a.AbsUrl("href"))).ToList()
            : new List<Episode>();
        var intro = doc.SelectFirst(".style-img.clearfix.pd10 > section")?.Select("p").ElementAtOrDefault(4).Text();
        return new BookDetail(list, intro);
    }

    public override AudioExtractor GetAudioExtractor() => VarNowExtractor(desktop: true);
}

/// <summary>乐听吧 leting8.com</summary>
public sealed class LetingBa : PageBarCmsSource
{
    public override string Id => "f04408546017411c890d4d72325a67ec";
    public override string Name => "乐听吧";
    protected override string Site => "https://www.leting8.com/";
    public override string Description => "推荐指数:3星 ⭐⭐⭐\n搜索可能需要验证：在搜索页点击该源旁的“验证”，输入验证码后关闭窗口即可。";
}
