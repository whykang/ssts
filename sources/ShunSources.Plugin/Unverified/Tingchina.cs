using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>听中国（电脑版）tings8.com</summary>
public sealed class Tingchina : ShunSource, ILoginSource
{
    // 原版与 Tingchina2 共用同一个 ID，这里换成新的 ID 以便两个源能同时存在
    public override string Id => "b3a1c6e0d2f94e5a8c7b9d1e2f3a4b51";
    public override string Name => "听中国（电脑版）";
    public override string Url => "https://www.tings8.com/";
    public override string Description => "推荐指数:2星 ⭐⭐\n此网站好像已关闭了，等开吧。";
    public override bool EnabledByDefault => false;
    protected override string CoverDomain => "www.tings8.com";

    public string LoginUrl => "https://www.tings8.com/pc/login/index.html";
    public bool LoginDesktop => true;

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"https://www.tings8.com/pc/index/search.html?keyword={Enc(keywords)}&page={page}", true, ct);
        var (current, total, _) = ParsePager(doc);
        return new SearchResult(ParseBooks(doc), Math.Max(total, current));
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc(Url, true, ct);
        var tabs = doc.Select(".nav-ol > li > a, .quanben > li > a")
            .Where(a => a.Text() is not ("首页" or "留言求书"))
            .Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var (current, total, next) = ParsePager(doc);
        return new CategoryPage(ParseBooks(doc), current, total, url, next);
    }

    internal static (int Current, int Total, string? Next) ParsePager(IDocument doc)
    {
        var current = RegexInt(doc.Select(".pagination > .active > span").Select(e => e.Text()).FirstOrDefault(), @"(\d+)");
        var next = doc.Select(".pagination > li > a").FirstOrDefault(a => a.Text().Contains('»'));
        return (current, next != null ? current + 1 : current, next?.AbsUrl("href"));
    }

    internal static List<Book> ParseBooks(IDocument doc)
    {
        var books = new List<Book>();
        foreach (var li in doc.Select(".list-works > li"))
        {
            var a = li.SelectFirst(".list-book-dt > a");
            if (a == null) continue;
            var authors = li.Select(".list-works-dl > .list-book-cs > .book-author");
            books.Add(new Book(li.SelectFirst(".list-imgbox > .thumb > .lazy").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                authors.ElementAtOrDefault(0).Text(), authors.ElementAtOrDefault(1).Text())
            {
                Intro = li.SelectFirst(".list-works-dl > .list-book-des").Text(),
                Status = li.SelectFirst(".list-book-dt > .ztlz").Text(),
            });
        }
        return books;
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await GetDoc(bookUrl, true, ct);
        var episodes = doc.Select("#playlist a").Select(a => new Episode(a.Text(), a.AbsUrl("href"))).ToList();
        return new BookDetail(episodes, doc.SelectFirst(".book-des > .div-b").OwnText());
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.WebViewSniff(ContainsAudioExt, desktop: true);
}
