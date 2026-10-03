using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>一夜幻听 22ting.com</summary>
public sealed class YiyeHuanting : ShunSource, ISearchVerification
{
    public override string Id => "488676f5ccf8455aa7770cbd197fd500";
    public override string Name => "一夜幻听";
    public override string Url => "https://22ting.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n搜索可能需要验证：在搜索页点击该源旁的“验证”，输入验证码后关闭窗口即可。";
    protected override string CoverDomain => "22ting.com/";

    public string GetSearchVerificationUrl(string keywords) => $"https://22ting.com/search.php?searchword={Enc(keywords)}";

    // 网站限制两次搜索的间隔（原版同样延迟 6 秒）
    public int SearchDelaySeconds => 6;

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"https://22ting.com/search.php?page={page}&searchword={Enc(keywords)}&searchtype=", true, ct, withCookies: true);
        var books = new List<Book>();
        foreach (var li in doc.Select(".row-b > li"))
        {
            var a = li.SelectFirst(".clearfix > section > .mb5 > a");
            if (a == null) continue;
            books.Add(new Book(li.SelectFirst(".clearfix > a > span > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                li.SelectFirst(".clearfix > section > .mb5 > .f-gray").Text())
            {
                Intro = li.SelectFirst(".clearfix > section > .mb5 > .f-12").Text(),
                Status = li.SelectFirst(".clearfix > section")?.Select("p").ElementAtOrDefault(1).Text() ?? "",
            });
        }
        var last = doc.SelectFirst(".mb15 > span") is { } bar ? FindByText(bar, "a", "尾页") : null;
        return new SearchResult(books, RegexInt(last.AbsUrl("href"), @"page=(\d+)", page));
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc(Url, true, ct);
        var nav = doc.Select(".clearfix").ElementAtOrDefault(1);
        var tabs = nav?.Select("li > a").Where(a => a.Text() != "首页").Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList()
                   ?? new List<CategoryTab>();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var current = RegexInt(doc.Select(".mb15 > span > .now-page").Select(e => e.Text()).FirstOrDefault(), @"(\d+)");
        var bar = doc.SelectFirst(".mb15 > span");
        var total = RegexInt(bar == null ? null : FindByText(bar, "a", "尾页").AbsUrl("href"), @"page=(\d+)", current);
        var next = bar == null ? null : FindByText(bar, "a", "››")?.AbsUrl("href");
        var books = new List<Book>();
        foreach (var li in doc.Select(".row-b > li"))
        {
            var a = li.SelectFirst(".clearfix > section > .mb5 > a");
            if (a == null) continue;
            var ps = li.Select(".clearfix > section > p");
            books.Add(new Book(li.SelectFirst(".clearfix > a > .img-box > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                li.SelectFirst(".clearfix > section > .mb5 > span").Text())
            {
                Intro = ps.ElementAtOrDefault(0).Text(),
                Status = ps.ElementAtOrDefault(1)?.SelectFirst("a").Text() ?? "",
            });
        }
        return new CategoryPage(books, current, Math.Max(total, next != null ? current + 1 : current), url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await GetDoc(bookUrl, true, ct);
        var list = loadEpisodes
            ? doc.Select("#yuedu .clearfix > li a").Select(a => new Episode(a.Text(), a.AbsUrl("href"))).ToList()
            : new List<Episode>();
        var intro = doc.SelectFirst(".pd10 > section")?.Select("p").ElementAtOrDefault(4).Text();
        return new BookDetail(list, intro);
    }

    public override AudioExtractor GetAudioExtractor() => VarNowExtractor(desktop: true);
}
