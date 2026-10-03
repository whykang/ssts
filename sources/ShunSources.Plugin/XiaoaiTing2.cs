using System.Text.RegularExpressions;
using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 爱听书（电脑版）www.itingshu.net。
/// 列表、搜索结果带封面、简介、作者、演播和连载状态；目录每页 50 集（?page=N&amp;sort=asc），目录页和播放页有一层 JS 校验。
/// 类名保留原来的 XiaoaiTing2、ID 不变，书架里手机版地址（m.itingshu.net）的书会自动转成电脑版。
/// </summary>
public sealed partial class XiaoaiTing2 : ShunSource, ILoginSource, IIncrementalEpisodes
{
    private const string Site = "https://www.itingshu.net/";

    public override string Id => "e30009d5e6714d89a2692666ddf13cbd";
    public override string Name => "爱听书";
    public override string Url => Site;
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n有声小说、长篇评书、相声、百家讲坛等。网站限制访问频率，章节很多的书需要一点时间才能加载完。不能播放时先在插件页面“登录”。";
    public override bool IsMultipleEpisodePages => true;
    protected override string CoverDomain => "itingshu.net";

    public string LoginUrl => Site + "user/public/login.html";

    /// <summary>搜索结果第 2 页起的地址模板（第一页的“下页”链接把页码换成 {0}），按关键词记住</summary>
    private readonly Dictionary<string, string> _searchPageUrl = new();

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        IDocument doc;
        if (page > 1 && _searchPageUrl.TryGetValue(keywords, out var template))
        {
            // 不带 Referer 时返回“友情提示信息”页
            doc = await GetDoc(string.Format(template, page), true, ct, referer: Site + "novelsearch/search/result.html");
        }
        else
        {
            doc = await Host.GetHtmlAsync(Site + "novelsearch/search/result.html", new RequestOptions
            {
                Desktop = true,
                Method = "POST",
                Body = "searchword=" + Enc(keywords),
                // 不带 Referer 和 Origin 时网站收不到关键词
                Headers = new Dictionary<string, string> { ["Referer"] = Site, ["Origin"] = Site.TrimEnd('/') },
                UseWebViewCookies = true,
            }, ct);
            var next = NextPageUrl(doc);
            if (next != null) _searchPageUrl[keywords] = PageNumber().Replace(next, "/{0}.html");
        }
        return new SearchResult(ParseBooks(doc), Math.Max(page, LastPage(doc)));
    }

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        static CategoryTab T(string title, string code) => new(title, $"{Site}yousheng/{code}/lastupdate/1/1.html");
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("有声小说", new[]
            {
                T("全部", "all"), T("玄幻修真", "xuanhuan"), T("都市言情", "dushi"), T("灵异惊悚", "lingyi"), T("军事历史", "junshi"),
                T("网游竞技", "jingji"), T("官场商战", "guanchangshangzhan"), T("通俗文学", "wenxue"), T("经典纪实", "jishi"),
                T("人物传记", "chuanji"), T("儿童故事", "ertong"), T("其他有声", "qita"),
            }),
            new CategoryMenu("评书曲艺", new[]
            {
                T("长篇评书", "pingshu"), T("相声戏曲", "xiangsheng"), T("百家讲坛", "bjjt"), T("综艺娱乐", "yule"),
            }),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var current = RegexInt(url, @"/(\d+)\.html$");
        return new CategoryPage(ParseBooks(doc), current, Math.Max(current, LastPage(doc)), url, NextPageUrl(doc));
    }

    private static string? NextPageUrl(IDocument doc) =>
        doc.Select("a").FirstOrDefault(a => a.Text() is "下页" or "下一页")?.AbsUrl("href") is { Length: > 0 } u ? u : null;

    private static int LastPage(IDocument doc) =>
        RegexInt(doc.Select("a").FirstOrDefault(a => a.Text() == "尾页")?.AbsUrl("href"), @"/(\d+)\.html", 1);

    [GeneratedRegex(@"/\d+\.html$")]
    private static partial Regex PageNumber();

    /// <summary>分类和搜索结果：ul.list-works &gt; li</summary>
    internal static List<Book> ParseBooks(IDocument doc)
    {
        var books = new List<Book>();
        foreach (var li in doc.Select("ul.list-works > li"))
        {
            var a = li.SelectFirst("dt.list-book-dt > a");
            if (a == null) continue;
            var img = li.SelectFirst(".list-imgbox img");
            var cover = img?.AbsUrl("data-original") is { Length: > 0 } c ? c : img?.AbsUrl("src") ?? "";
            var state = li.SelectFirst("dt.list-book-dt > span").Text();
            var latest = li.SelectFirst("dt.list-book-dt > span > a").Text();
            books.Add(new Book(cover, a.AbsUrl("href"), a.Text(),
                string.Join(" ", li.Select(".book-author a").Select(e => e.Text())),
                string.Join(" ", li.Select(".book-boyin a").Select(e => e.Text())))
            {
                Intro = li.SelectFirst("dd.list-book-des").Text(),
                Status = string.Join(" · ", new[] { state, latest }.Where(x => x.Length > 0)),
                IsCompleted = state.Contains("完结"),
            });
        }
        return books;
    }

    private const int PageSize = 50;

    public override Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct) =>
        LoadAsync(bookUrl, null, loadEpisodes, loadFullPages, ct);

    /// <summary>
    /// 已有章节时只做增量更新：书页上的“最新列表”（最近 10 集，不需要校验）能接上缓存就直接合并；
    /// 接不上（更新太多，或上次因为限流没加载完整）就从缓存断开的那一页接着加载，不从头来。
    /// </summary>
    public Task<BookDetail?> UpdateEpisodesAsync(string bookUrl, IReadOnlyList<Episode> known, CancellationToken ct) =>
        LoadAsync(bookUrl, known, true, true, ct)!;

    private async Task<BookDetail> LoadAsync(string bookUrl, IReadOnlyList<Episode>? known, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        // 书架里旧的手机版地址
        bookUrl = bookUrl.Replace("://m.itingshu.net/", "://www.itingshu.net/");
        var list = new List<Episode>();
        var doc1 = await GetDoc(bookUrl, true, ct);
        var artist = doc1.Select(".book-info dd").FirstOrDefault(d => d.Text().StartsWith("演播"))?.Select("a").Select(e => e.Text()).ToList();
        var detail = new BookDetail(list, doc1.SelectFirst(".book-des").Text())
        {
            CoverUrl = doc1.SelectFirst(".book-info")?.ParentElement?.SelectFirst("img")?.AbsUrl("data-original"),
            Artist = artist is { Count: > 0 } ? string.Join(" ", artist) : null,
        };
        if (!loadEpisodes) return detail;

        if (known is { Count: > 0 })
        {
            // 最新列表是倒序的，翻成正序后找和缓存的衔接点
            var latest = new List<Episode>();
            AddEpisodes(doc1, latest);
            latest.Reverse();
            var urls = known.Select(e => e.Url).ToHashSet();
            if (latest.Count > 0 && urls.Contains(latest[0].Url))
            {
                list.AddRange(known);
                list.AddRange(latest.Where(e => !urls.Contains(e.Url)));
                return detail;
            }
        }

        // 书页上的“查看全部章节”进入目录（每页 50 集），目录页上的“快速选集”列出全部分页
        var dirUrl = doc1.SelectFirst("a.dirurl").AbsUrl("href");
        var doc = dirUrl.Length > 0 ? await GetDirAsync(dirUrl, ct) : doc1;
        var pages = doc.Select("a")
            .Select(a => a.AbsUrl("href"))
            .Select(u => (Url: u, Page: RegexInt(u, @"[?&]page=(\d+)", 0)))
            .Where(p => p.Page > 1)
            .GroupBy(p => p.Page).Select(g => g.First())
            .OrderBy(p => p.Page).Select(p => p.Url).ToList();

        // 续传：保留缓存里完整的前几页，从下一页开始
        var startPage = 1;
        if (known is { Count: > PageSize } && pages.Count > 0)
        {
            startPage = Math.Min(known.Count / PageSize, pages.Count) + 1;
            list.AddRange(known.Take((startPage - 1) * PageSize));
        }
        if (startPage == 1) AddEpisodes(doc, list);
        if (!loadFullPages) return detail;

        // 网站按请求头限流很严（几十秒内十来次就返回 429“请求过于频繁”），只能逐页慢慢加载
        try
        {
            for (var page = Math.Max(2, startPage); page <= pages.Count + 1; page++)
            {
                Host.ReportProgress($"{page} / {pages.Count + 1}");
                await Task.Delay(6000, ct);
                AddEpisodes(await GetDirAsync(pages[page - 2], ct), list);
            }
        }
        catch (RateLimitedException)
        {
            // 被限流太久：先返回已加载的部分，下次打开这本书时从断开处接着加载
            Host.Toast($"爱听书限制访问频率，先加载了 {list.Count} 集，下次打开这本书时会接着加载");
        }
        finally
        {
            Host.ReportProgress(null);
        }
        return detail;
    }

    // 目录页的访问令牌：网站先返回一段脚本，把令牌写进 __51guid__ Cookie 后刷新页面。
    // 令牌是“IP|ID|过期时间|签名”，2 小时内对所有目录页有效，所以解出来以后直接带上 Cookie 请求，不需要 WebView。
    private static string? _dirToken;
    private static long _dirTokenExpires;

    private async Task<IDocument> GetDirAsync(string url, CancellationToken ct)
    {
        for (int attempt = 0, limited = 0; attempt < 3; attempt++)
        {
            var token = DateTimeOffset.UtcNow.ToUnixTimeSeconds() < Interlocked.Read(ref _dirTokenExpires) ? _dirToken : null;
            string html;
            try
            {
                html = await Host.GetStringAsync(url, new RequestOptions
                {
                    Desktop = true,
                    Headers = token != null ? new Dictionary<string, string> { ["Cookie"] = $"__51guid__={Uri.EscapeDataString(token)}" } : null,
                }, ct);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                if (limited >= 5) throw new RateLimitedException();
                // 请求太快被限流：等一分钟再试（不算在令牌重试次数里）
                limited++;
                for (var s = 60; s > 0; s--)
                {
                    Host.ReportProgress($"（网站限流，{s} 秒后继续）");
                    await Task.Delay(1000, ct);
                }
                attempt--;
                continue;
            }
            if (!html.Contains("reversed = \"")) return Host.ParseHtml(html, url);
            if (!TryReadToken(html, out var newToken, out var expires))
                break;
            _dirToken = newToken;
            Interlocked.Exchange(ref _dirTokenExpires, expires - 300);
        }
        // 校验方式变了时退回 WebView 渲染
        var rendered = await Host.RenderAsync(url, true,
            "(function(){return document.querySelector('#playlist li a')?'<html>'+document.documentElement.innerHTML+'</html>':'';})();",
            TimeSpan.FromSeconds(25), ct);
        return Host.ParseHtml(rendered, url);
    }

    /// <summary>页面脚本：var reversed = "倒序的 base64"，解码后是 var token = '...'</summary>
    private static bool TryReadToken(string html, out string token, out long expires)
    {
        token = "";
        expires = 0;
        try
        {
            var reversed = Regex.Match(html, "reversed = \"([^\"]+)\"").Groups[1].Value;
            var b64 = new string(reversed.Reverse().ToArray());
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var code = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            token = Regex.Match(code, @"var token = '([^']+)'").Groups[1].Value;
            if (token.Length == 0) return false;
            // 令牌本身是 base64(“IP|ID|过期时间|签名”)
            var t = token.PadRight(token.Length + (4 - token.Length % 4) % 4, '=');
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t)).Split('|');
            expires = parts.Length > 2 && long.TryParse(parts[2], out var e) ? e : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class RateLimitedException() : InvalidOperationException("爱听书限制了访问频率（请求过于频繁），请过几分钟再试");

    private static void AddEpisodes(IDocument doc, List<Episode> list)
    {
        // 标题带广告尾巴：“第001集浓雾_催更V裙vw5418814524”
        foreach (var a in doc.Select("#playlist li a"))
            list.Add(new Episode(Regex.Replace(a.Text(), @"[_\s]*催更.*$", ""), a.AbsUrl("href")));
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.WebViewSniff(desktop: true); // 播放页同样有 JS 校验，交给 WebView 打开并嗅探（电脑版用 jPlayer）
}
