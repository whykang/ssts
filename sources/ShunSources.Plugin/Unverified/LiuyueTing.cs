using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>六月听书 tingshu168.com（电脑版）</summary>
public sealed class LiuyueTing : ShunSource
{
    public override string Id => "d8fba9f19df2465198425c7bc3861de8";
    public override string Name => "六月听书";
    public override string Url => "http://www.tingshu168.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n【已失效】网站目前无法连接，源保留以便网站恢复。";
    public override bool IsMultipleEpisodePages => true;
    public override bool EnabledByDefault => false;
    protected override string CoverDomain => "tingshu168.com";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var json = await Host.GetJsonAsync($"http://www.tingshu168.com/search/index/search?content={Enc(keywords)}&type=1&pageNum={page}&pageSize=10", ct: ct);
        var books = json.Items("data.content").Select(b => LiuyueJson.ParseBook(b, "http://img.tingshu168.com:20001/", "http://www.tingshu168.com/list/")).ToList();
        return new SearchResult(books, Math.Max(1, json.Int("data.totalPages")));
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc("http://www.tingshu168.com/ys/t1", true, ct);
        var tabs = doc.Select(".category-list li a").Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var current = RegexInt(url, @"p(\d+)");
        var total = RegexInt(doc.SelectFirst(".pagination > .last").Text(), @"(\d+)", current);
        var next = doc.SelectFirst(".pagination") is { } p ? FindByText(p, "a", "下一页")?.AbsUrl("href") : null;
        var books = new List<Book>();
        foreach (var li in doc.Select(".album-list > li"))
        {
            var a = li.SelectFirst(".book-item-r > .book-item-name > a");
            if (a == null) continue;
            var author = li.SelectFirst(".book-item-r > .book-item-info > .no-author")?.Text()
                         ?? li.SelectFirst(".book-item-r > .book-item-info > .author")?.Text() ?? "";
            var links = li.SelectFirst(".book-item-r > .book-item-info")?.Select("a").ToList() ?? new List<IElement>();
            var artist = links.Count > 1 ? links[1].Text() : links.FirstOrDefault().Text();
            books.Add(new Book(li.SelectFirst(".lf > a > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(), author, artist)
            {
                Intro = li.SelectFirst(".book-item-r > .weaken").Text(),
                Status = li.SelectFirst(".book-item-r > .book-item-status").Text(),
            });
        }
        return new CategoryPage(books, current, total, url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (!loadEpisodes) return new BookDetail(list);
        var doc = await GetDoc(bookUrl, true, ct);
        AddEpisodes(doc, list);
        var hasNext = doc.Select(".pagination > .next").Any(e => e.Text().Contains("下一页"));
        var total = RegexInt(doc.SelectFirst(".pagination > .page.last").Text(), @"(\d+)");
        if (loadFullPages && hasNext)
        {
            var baseUrl = bookUrl.Replace("/p1", "").Replace("/p", "");
            for (var p = 2; p <= total; p++)
            {
                ct.ThrowIfCancellationRequested();
                Host.ReportProgress($"{p} / {total}");
                AddEpisodes(await GetDoc($"{baseUrl}/p{p}", true, ct), list);
                await Task.Delay(Random.Shared.Next(500, 1000), ct);
            }
            Host.ReportProgress(null);
        }
        return new BookDetail(list);
    }

    private static void AddEpisodes(IDocument doc, List<Episode> list)
    {
        foreach (var a in doc.Select(".play-list > ul li a")) list.Add(new Episode(a.Text(), a.AbsUrl("href")));
    }

    // 原版脚本只返回 iframe 的 document，这里直接取出 iframe 内 audio 的地址
    public override AudioExtractor GetAudioExtractor() => AudioExtractor.WebView(r => r.Trim('"'), desktop: false,
        script: "(function(){var f=document.getElementById('myIframe');var d=(f&&f.contentDocument)||document;var a=d.getElementById('audio');return a?a.src:'';})();");
}

/// <summary>六月听书 6yueting.com（手机版）</summary>
public sealed class LiuyueTingmobile : ShunSource
{
    private static readonly Dictionary<string, string> MobileHeaders = new() { ["Referer"] = "http://m.6yueting.com" };

    public override string Id => "c38b9f44c5374da9bfca4bb90d6ab383";
    public override string Name => "六月听书（手机版）";
    public override string Url => "http://m.tingshu168.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n【已失效】网站目前无法连接，源保留以便网站恢复。";
    public override bool EnabledByDefault => false;
    protected override string CoverDomain => "m.6yueting.com/";
    protected override string Referer => "http://m.6yueting.com/";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var json = await Host.GetJsonAsync($"http://m.6yueting.com/search/index/search?content={Enc(keywords)}&type=1&pageNum={page}&pageSize=40", ct: ct);
        var books = json.Items("data.content").Select(b => LiuyueJson.ParseBook(b, "http://img.6yueting.com:20001/", "http://m.6yueting.com/list/")).ToList();
        var total = json.Int("data.totalPages");
        return new SearchResult(books, total > 0 ? total : (int)Math.Ceiling(json.Int("code") / 40.0));
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc("http://m.tingshu168.com/ys", false, ct);
        var tabs = doc.Select(".type-list > li a").Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, false, ct);
        var pager = doc.SelectFirst(".all-list > .pagination.page-width > span").Text();
        var current = RegexInt(pager, @"(\d+)/\d+");
        var total = RegexInt(pager, @"\d+/(\d+)", current);
        var next = doc.SelectFirst(".all-list > .pagination.page-width") is { } p ? FindByText(p, "a", "下一页")?.AbsUrl("href") : null;
        var books = new List<Book>();
        foreach (var a in doc.Select(".list-wrapper > ul a"))
        {
            var broadcaster = a.SelectFirst(".text > div > .broadcaster");
            var artist = broadcaster?.QuerySelector("i.icon-broad")?.NextSibling?.TextContent.Trim() ?? broadcaster.Text();
            books.Add(new Book(a.SelectFirst(".icon > img").AbsUrl("src"), a.AbsUrl("href"), a.SelectFirst(".text > .name").Text(),
                a.SelectFirst(".item").Attr("data-author"), artist)
            {
                Intro = a.SelectFirst(".text > .desc").Text(),
            });
        }
        return new CategoryPage(books, current, total, url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await Host.GetHtmlAsync(bookUrl, new RequestOptions { Headers = MobileHeaders }, ct);
        var list = loadEpisodes
            ? doc.Select(".book-list.clearfix a").Select(a => new Episode(a.Text(), a.AbsUrl("href"))).ToList()
            : new List<Episode>();
        return new BookDetail(list, doc.SelectFirst(".book-intro.tab-cont").Text());
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.WebViewSniff();
}

internal static class LiuyueJson
{
    public static Book ParseBook(System.Text.Json.Nodes.JsonNode b, string coverPrefix, string bookPrefix)
    {
        var finished = b.Int("state") == 2;
        var title = b.Str("name").Replace("<span style=\"color:red\">", "").Replace("</span>", "");
        return new Book(coverPrefix + b.Str("coverUrlLocal"), bookPrefix + b.Str("code"), title, b.Str("author"), b.Str("broadcaster"))
        {
            Intro = b.Str("descXx"),
            Status = (finished ? "完结|" : "更新到") + b.Int("trackTotalCount") + "集",
            IsCompleted = finished,
        };
    }
}
