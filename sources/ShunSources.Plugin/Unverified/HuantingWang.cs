using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>幻听网 huanting.cc</summary>
public sealed class HuantingWang : ShunSource, IAudioHeaders
{
    public override string Id => "4c229e17e7104a90947edbb7f182bd1f";
    public override string Name => "幻听网";
    public override string Url => "https://www.huanting.cc/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n【已失效】huanting.cc 域名目前已改作其它网站，源保留以便网站恢复。";
    public override bool IsMultipleEpisodePages => true;
    public override bool EnabledByDefault => false;
    protected override string CoverDomain => "huanting.cc";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"https://www.huanting.cc/Ps.php?q={Enc(keywords)}&page={page}", true, ct);
        var books = new List<Book>();
        foreach (var li in doc.Select(".content_left .result"))
        {
            var a = li.SelectFirst(".title > a");
            if (a == null) continue;
            var last = li.Select(".last");
            books.Add(new Book(li.SelectFirst(".image_pic > a > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(), "",
                last.ElementAtOrDefault(0)?.SelectFirst("span > a").Text() ?? "")
            {
                Intro = last.ElementAtOrDefault(1).Text(),
                Status = last.ElementAtOrDefault(2)?.SelectFirst("span").Text() ?? "",
            });
        }
        var current = RegexInt(doc.QuerySelector("#page .current").Text(), @"(\d+)");
        var hasLast = doc.QuerySelector("#page") is { } p && FindByText(p, "a", "尾页") != null;
        return new SearchResult(books, hasLast ? current + 1 : current);
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc("https://www.huanting.cc/", true, ct);
        var tabs = doc.Select(".mainnav > ul > li > a")
            .Where(a => a.Text() != "听书首页")
            .Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var current = RegexInt(doc.SelectFirst(".list_box > .page > .current").Text(), @"(\d+)");
        var next = doc.SelectFirst(".list_box > .page") is { } pager ? FindByText(pager, "a", "下一页") : null;
        var books = new List<Book>();
        foreach (var box in doc.Select(".list_box > .book"))
        {
            var a = box.SelectFirst(".left > dt > a");
            if (a == null) continue;
            books.Add(new Book(box.SelectFirst(".img.left > .lazy").AbsUrl("data-original"), a.AbsUrl("href"), a.Text(), "",
                box.SelectFirst(".left > .zb").Text())
            {
                Intro = box.SelectFirst(".left > .info > a").Text(),
                Status = box.SelectFirst(".left > .zt").Text(),
            });
        }
        return new CategoryPage(books, current, next != null ? current + 1 : current, url, next?.AbsUrl("href"));
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (!loadEpisodes) return new BookDetail(list);
        var doc = await GetDoc(bookUrl, true, ct);
        AddEpisodes(doc, list);
        var pages = doc.Select(".play_navs a").Select(a => a.AbsUrl("href")).Where(u => u.Length > 0).Distinct().ToList();
        var total = RegexInt(doc.Select(".list_book > .play_navs a").LastOrDefault().AbsUrl("href"), @"p=(\d+)");
        if (loadFullPages && total > 1 && pages.Count > 1)
        {
            var i = 1;
            foreach (var pageUrl in pages.Skip(1))
            {
                ct.ThrowIfCancellationRequested();
                Host.ReportProgress($"{++i} / {total}");
                AddEpisodes(await GetDoc(pageUrl, true, ct), list);
                await Task.Delay(Random.Shared.Next(1000, 1500), ct);
            }
            Host.ReportProgress(null);
        }
        return new BookDetail(list);
    }

    private static void AddEpisodes(IDocument doc, List<Episode> list)
    {
        foreach (var a in doc.Select("#vlink li a"))
            list.Add(new Episode(a.Text(), a.AbsUrl("href")));
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.WebViewSniff(desktop: true);

    public IDictionary<string, string>? GetAudioHeaders(string audioUrl)
    {
        if (!audioUrl.Contains("huanting")) return null;
        return new Dictionary<string, string>
        {
            ["Referer"] = "https://www.huanting.cc/",
            ["Cookie"] = Host.GetWebViewCookiesAsync("https://www.huanting.cc/").GetAwaiter().GetResult(),
        };
    }
}
