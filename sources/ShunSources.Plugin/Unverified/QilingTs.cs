using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>麒麟听书 70ts.com</summary>
public sealed class QilingTs : ShunSource
{
    public override string Id => "513215a82fb04abfbaf80066fea9e78f";
    public override string Name => "麒麟听书";
    public override string Url => "https://www.70ts.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n【已失效】网站目前返回 404，源保留以便网站恢复。";
    public override bool EnabledByDefault => false;
    public override bool IsMultipleEpisodePages => true;
    protected override string CoverDomain => "www.70ts.com/";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"https://www.70ts.com/so/search.html?searchtype=name&searchword={Enc(keywords)}&page={page}", true, ct);
        var books = new List<Book>();
        foreach (var li in doc.Select(".list-works li"))
        {
            var a = li.SelectFirst(".list-works-dl > .list-book-dt > a");
            if (a == null) continue;
            var authors = li.Select(".list-works-dl > .list-book-cs > .book-author");
            books.Add(new Book(li.SelectFirst(".list-imgbox > a > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                authors.ElementAtOrDefault(0).Text(), authors.ElementAtOrDefault(1).Text())
            {
                Intro = li.SelectFirst(".list-works-dl > .list-book-des").Text(),
                Status = authors.ElementAtOrDefault(2)?.SelectFirst("a").Text() ?? "",
            });
        }
        var (current, total, _) = ParsePager(doc);
        return new SearchResult(books, Math.Max(current, total));
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc(Url, true, ct);
        var tabs = doc.Select(".nav-ol > li > a").Where(a => a.Text() != "首页").Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var (current, total, next) = ParsePager(doc);
        var books = new List<Book>();
        foreach (var li in doc.Select(".list-works > li"))
        {
            var a = li.SelectFirst(".list-works-dl > .list-book-dt > a");
            if (a == null) continue;
            var authors = li.Select(".list-works-dl > .list-book-cs > .book-author");
            books.Add(new Book(li.SelectFirst(".list-imgbox > a > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                authors.ElementAtOrDefault(0)?.SelectFirst("a").Text() ?? "", authors.ElementAtOrDefault(1)?.SelectFirst("a").Text() ?? "")
            {
                Intro = li.SelectFirst(".list-works-dl > .list-book-des").Text(),
                Status = li.SelectFirst(".list-works-dl > .list-book-dt > span").Text(),
            });
        }
        return new CategoryPage(books, current, total, url, next);
    }

    private static (int Current, int Total, string? Next) ParsePager(IDocument doc)
    {
        var current = RegexInt(doc.Select(".fanye > strong").LastOrDefault().Text(), @"(\d+)");
        var next = doc.Select(".fanye > a").FirstOrDefault(a => a.Text().Contains("下一页"));
        return (current, next != null ? current + 1 : current, next?.AbsUrl("href"));
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (!loadEpisodes) return new BookDetail(list);
        var doc = await GetDoc(bookUrl, true, ct);
        AddEpisodes(doc, list);
        var hasNext = doc.Select(".jump-list > .pg-next").Any(e => e.Text().Contains("下一页"));
        if (loadFullPages && hasNext)
        {
            var pages = doc.Select(".hd-sel > select > option").Select(o => o.AbsUrl("value")).Where(u => u.Length > 0).ToList();
            for (var i = 1; i < pages.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Host.ReportProgress($"{i + 1} / {pages.Count}");
                AddEpisodes(await GetDoc(pages[i], true, ct), list);
                await Task.Delay(Random.Shared.Next(100, 500), ct);
            }
            Host.ReportProgress(null);
        }
        return new BookDetail(list);
    }

    private static void AddEpisodes(IDocument doc, List<Episode> list)
    {
        foreach (var a in doc.Select("#playlist li a")) list.Add(new Episode(a.Text(), a.AbsUrl("href")));
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.WebViewSniff(ContainsAudioExt, desktop: true);
}
