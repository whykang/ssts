using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>爱听书电脑版 www.itingshu.net 的公共实现</summary>
public abstract class XiaoaiTingPcBase : ShunSource, ILoginSource
{
    public override string Url => "https://www.itingshu.net";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n不能播放是网站的问题。不能播放时先在插件页面“登录”，还不行就换个网络。";
    public override bool IsMultipleEpisodePages => true;
    protected override string CoverDomain => "itingshu.net";
    protected override string Referer => "https://www.itingshu.net/";

    public string LoginUrl => "https://www.itingshu.net/user/public/login.html";
    public bool LoginDesktop => true;

    /// <summary>附加的请求头（ssl 版本使用了一组更完整的浏览器请求头）</summary>
    protected virtual Dictionary<string, string> ExtraHeaders => new() { ["Referer"] = "https://www.itingshu.net/" };

    protected Task<IDocument> Get(string url, CancellationToken ct) =>
        Host.GetHtmlAsync(url, new RequestOptions { Desktop = true, Headers = ExtraHeaders, UseWebViewCookies = true }, ct);

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await Host.GetHtmlAsync("https://www.itingshu.net/novelsearch/search/result.html", new RequestOptions
        {
            Desktop = true,
            Method = "POST",
            Body = "searchword=" + Enc(keywords),
            Headers = ExtraHeaders,
            UseWebViewCookies = true,
        }, ct);
        return new SearchResult(ParseBooks(doc), RegexInt(doc.SelectFirst(".fanye > span").Text(), @"(\d+)"));
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await Get("https://www.itingshu.net/yousheng/all.html", ct);
        var novels = doc.Select(".top-ul > li").FirstOrDefault()?.Select("dl > dd a")
            .Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList() ?? new List<CategoryTab>();
        var pingshu = new List<CategoryTab>();
        try
        {
            // 评书分类页（原地址 /boyin/485/ 目前已 404，失败时忽略）
            var doc2 = await Get("https://www.itingshu.net/boyin/485/", ct);
            pingshu = doc2.Select(".module-tab h3 a").Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        }
        catch (HttpRequestException) { }
        return new[] { new CategoryMenu("有声小说", novels), new CategoryMenu("长篇评书", pingshu) }.Where(m => m.Tabs.Count > 0).ToList();
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await Get(url, ct);
        var current = RegexInt(doc.SelectFirst(".fanye > strong").Text(), @"(\d+)");
        var links = doc.Select(".fanye a").ToList();
        var total = RegexInt(links.FirstOrDefault(a => a.Text().Contains("尾页")).AbsUrl("href"), @"page=(\d+)", current);
        var next = links.FirstOrDefault(a => a.Text().Contains("下页") || a.Text().Contains("下一页"))?.AbsUrl("href");
        return new CategoryPage(ParseBooks(doc), current, Math.Max(total, next != null ? current + 1 : current), url, next);
    }

    private static List<Book> ParseBooks(IDocument doc)
    {
        var books = new List<Book>();
        foreach (var li in doc.Select(".list-works > li"))
        {
            var a = li.SelectFirst(".list-works-dl > .list-book-dt > a");
            if (a == null) continue;
            books.Add(new Book(li.SelectFirst(".list-imgbox > .thumb > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                string.Join(" ", li.Select(".list-works-dl > .list-book-cs > .book-author > a").Select(e => e.Text())),
                string.Join(" ", li.Select(".list-works-dl > .list-book-cs > .book-boyin > a").Select(e => e.Text())))
            {
                Intro = li.SelectFirst(".list-works-dl > .list-book-des").Text(),
                Status = li.SelectFirst(".list-works-dl > .list-book-dt > span").Text(),
            });
        }
        return books;
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (!loadEpisodes) return new BookDetail(list);
        var doc = await Get(bookUrl, ct);
        AddEpisodes(doc, list);
        var hasNext = doc.Select(".jump-list > .pg-next").Any(e => e.Text().Contains("下一页"));
        if (loadFullPages && hasNext)
        {
            var pages = doc.Select(".hd-sel > select > option").Select(o => o.AbsUrl("value")).Where(u => u.Length > 0).ToList();
            for (var i = 1; i < pages.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Host.ReportProgress($"{i + 1} / {pages.Count}");
                AddEpisodes(await Get(pages[i], ct), list);
                await Task.Delay(Random.Shared.Next(100, 500), ct);
            }
            Host.ReportProgress(null);
        }
        return new BookDetail(list);
    }

    private static void AddEpisodes(IDocument doc, List<Episode> list)
    {
        foreach (var a in doc.Select("#playlist ul > li a")) list.Add(new Episode(a.Text(), a.AbsUrl("href")));
    }

    // 电脑版播放页的播放器脚本经过混淆、需要交互才加载；手机版同一路径的播放页可直接嗅探到音频
    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom((url, ct) =>
        Host.SniffMediaAsync(url.Replace("://www.itingshu.net", "://m.itingshu.net"), false, ContainsAudioExt, TimeSpan.FromSeconds(30), ct));
}

/// <summary>爱听书（电脑版）—— 原 XiaoaiTingssl</summary>
public sealed class XiaoaiTingssl : XiaoaiTingPcBase
{
    public override string Id => "3aa11119c74448efbd26cd3d16038bbc";
    public override string Name => "爱听书（电脑版）";
    public override bool EnabledByDefault => false;

    protected override Dictionary<string, string> ExtraHeaders => new()
    {
        ["Referer"] = "https://www.itingshu.net",
        ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
        ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
    };
}

/// <summary>爱听书（电脑版备用）—— 原 XiaoaiTing1，与 XiaoaiTingssl 原本共用 ID，这里换成新 ID</summary>
public sealed class XiaoaiTing1 : XiaoaiTingPcBase
{
    public override string Id => "8d2e4f6a1b3c4d5e9f0a7b6c5d4e3f21";
    public override string Name => "爱听书（电脑版备用）";
    public override bool EnabledByDefault => false;
}
