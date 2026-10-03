using System.Text.RegularExpressions;
using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 相声随身听 xsmp3.com（与评书随身听同一套 Z-BlogPHP 主题）。
/// 每篇文章是一个“专辑”：全集分部（约 50 段）或单段相声，音频列表在 APlayer 脚本里。
/// 网站自带搜索在服务器端报错（gzinflate(): data error），所以抓取全站文章列表做本地索引来搜索。
/// </summary>
public sealed partial class Xsmp3 : ShunSource, IAudioHeaders
{
    private const string Site = "https://www.xsmp3.com/";
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromDays(7);

    private static readonly SemaphoreSlim IndexLock = new(1, 1);
    private static List<Entry>? _index;

    private sealed record Entry(string Url, string RawTitle, string Category);

    public override string Id => "5b0f3c1e9a7d4e26b8c4d1f0a2e7c913";
    public override string Name => "相声随身听";
    public override string Url => Site;
    public override string Description => "郭德纲、德云社、马三立、侯宝林、刘宝瑞、马季等相声全集。首次搜索需要建立索引（约半分钟），之后 7 天内搜索是即时的。";
    public override string Group => "相声";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var index = await GetIndexAsync(ct);
        var words = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = index.Where(e => words.All(w => e.RawTitle.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                                                     e.Category.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        const int pageSize = 30;
        var books = matches.Skip((page - 1) * pageSize).Take(pageSize).Select(ToBook).ToList();
        return new SearchResult(books, Math.Max(1, (matches.Count + pageSize - 1) / pageSize));
    }

    private static Book ToBook(Entry e)
    {
        var (title, artist) = CleanTitle(e.RawTitle);
        return new Book("", e.Url, title, "", artist.Length > 0 ? artist : e.Category) { Status = e.Category };
    }

    /// <summary>
    /// “马三立王凤山《白事会》相声在线收听,mp3免费下载” → (“白事会”, “马三立王凤山”)；
    /// “郭德纲于谦相声全集高清版(一)持续更新,最全合集!” → (“郭德纲于谦相声全集高清版(一)”, “”)
    /// </summary>
    private static (string Title, string Artist) CleanTitle(string raw)
    {
        var m = Regex.Match(raw, @"^(.*?)《(.+?)》");
        if (m.Success) return (m.Groups[2].Value, m.Groups[1].Value);
        var title = Regex.Replace(raw, @"(相声)?(在线收听|持续更新|,|，).*$", "");
        return (title.Length > 0 ? title : raw, "");
    }

    /// <summary>从磁盘缓存或全站文章列表（/page/N.html）建立索引</summary>
    private async Task<List<Entry>> GetIndexAsync(CancellationToken ct)
    {
        if (_index != null) return _index;
        await IndexLock.WaitAsync(ct);
        try
        {
            if (_index != null) return _index;
            var file = Path.Combine(Host.GetCacheDir("xsmp3"), "index.tsv");
            if (File.Exists(file) && DateTime.Now - File.GetLastWriteTime(file) < IndexLifetime)
            {
                var cached = File.ReadAllLines(file).Select(l => l.Split('\t')).Where(p => p.Length == 3)
                    .Select(p => new Entry(p[0], p[1], p[2])).ToList();
                if (cached.Count > 0) return _index = cached;
            }

            Host.ReportProgress("正在建立搜索索引…");
            var first = await GetDoc(Site, true, ct);
            var last = first.Select("a").Where(a => a.Text().Contains("尾页"))
                .Select(a => RegexInt(a.AbsUrl("href"), @"/page/(\d+)\.html", 1)).FirstOrDefault(1);
            var pages = new List<Entry>[last + 1];
            pages[1] = ParseList(first);
            var done = 1;
            using var gate = new SemaphoreSlim(6);
            var tasks = Enumerable.Range(2, Math.Max(0, last - 1)).Select(async p =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    for (var attempt = 0; ; attempt++)
                    {
                        try
                        {
                            pages[p] = ParseList(await GetDoc($"{Site}page/{p}.html", true, ct));
                            break;
                        }
                        catch (Exception) when (attempt < 2 && !ct.IsCancellationRequested)
                        {
                            await Task.Delay(1000, ct);
                        }
                    }
                    Host.ReportProgress($"正在建立搜索索引 {Interlocked.Increment(ref done)} / {last}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Host.Log($"相声随身听索引第 {p} 页失败：{ex.Message}");
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);

            var seen = new HashSet<string>();
            var list = pages.Where(x => x != null).SelectMany(x => x).Where(e => seen.Add(e.Url)).ToList();
            // 有页面失败时不写缓存，下次再完整重建
            if (pages.Skip(1).All(x => x != null))
                File.WriteAllLines(file, list.Select(e => $"{e.Url}\t{e.RawTitle}\t{e.Category}"));
            return _index = list;
        }
        finally
        {
            Host.ReportProgress(null);
            IndexLock.Release();
        }
    }

    private static List<Entry> ParseList(IDocument doc)
    {
        var list = new List<Entry>();
        foreach (var li in doc.Select(".post_list_li"))
        {
            var a = li.QuerySelector("h2 a");
            if (a == null) continue;
            var url = a.AbsUrl("href");
            if (!ArticleUrl().IsMatch(url)) continue;
            var category = li.QuerySelector(".fenli a")?.TextContent.Trim() ?? "";
            list.Add(new Entry(url, Clean(a.TextContent), Clean(category)));
        }
        return list;
    }

    private static string Clean(string s) => Regex.Replace(s, @"[\t\r\n]+", " ").Trim();

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        static CategoryMenu Menu(string title, params string[] tabs) =>
            new(title, tabs.Select(t => t.Split('=')).Select(p => new CategoryTab(p[0], $"{Site}{p[1]}.html")).ToList());

        IReadOnlyList<CategoryMenu> menus = new[]
        {
            Menu("郭德纲", "全部=gdg", "郭德纲单口=gdg-dk", "郭德纲于谦=gdg-yq", "郭德纲张文顺=gdg-zws", "郭德纲李菁=gdg-lj", "郭德纲徐德亮=gdg-xdl",
                "郭德纲何云伟=gdg-hyw", "郭德纲曹云金=gdg-cyj", "郭德纲王玥波=gdg-wyb", "郭德纲王文林=gdg-wwl"),
            Menu("德云社", "全部=dys", "德云社精选=dys-jx", "高峰=dys-gf", "岳云鹏=dys-yyp", "郭麒麟=dys-gql", "张鹤伦=dys-zhl", "孟鹤堂=dys-mht",
                "徐德亮王文林=xdl-wwl", "何云伟李菁=hyw-lj", "曹云金刘云天=cyj-lyt"),
            Menu("相声名家", "全部=xsmj", "马三立=msl", "侯宝林=hbl", "刘宝瑞=lbr", "马季=mj", "侯耀文=hyw", "师胜杰=ssj", "姜昆=jk", "马志明=mzm",
                "杨振华=yzh", "苏文茂=swm", "王谦祥=wqx", "高英培=gyp", "李伯祥=lbx", "郭全宝=gqb", "郭荣起=grq", "郝爱民=ham", "李增瑞=lzr",
                "常贵田=cgt", "李金斗=ljd", "张寿臣=zsc", "刘文亨=lwh", "魏文亮=wwl", "常宝霆=cbt", "赵振铎=zzd", "佟有为=tyw", "马敬伯=mjb",
                "于宝林=ybl", "奇志大兵=qzdb", "刘伟=lw", "牛群=nq", "赵伟洲=zwz"),
            Menu("更多", "最新更新=page/1", "相声新势力=xsxsl", "青曲社=qqs"),
        };
        return Task.FromResult(menus);
    }

    /// <summary>分类第 N 页为 /{code}/N.html，首页列表为 /page/N.html</summary>
    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var books = ParseList(doc).Select(ToBook).ToList();
        var current = RegexInt(url, @"/(\d+)\.html$", 1);
        var total = doc.Select("a").Where(a => a.Text().Contains("尾页"))
            .Select(a => RegexInt(a.AbsUrl("href"), @"/(\d+)\.html$", current)).FirstOrDefault(current);
        var next = doc.Select("a").FirstOrDefault(a => a.Text().Contains("下一页"))?.AbsUrl("href");
        return new CategoryPage(books, current, Math.Max(total, current), url, string.IsNullOrEmpty(next) ? null : next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var html = await Host.GetStringAsync(bookUrl, RequestOptions.Pc, ct);
        var doc = Host.ParseHtml(html, bookUrl);
        var intro = doc.QuerySelector(".news_con p")?.TextContent.Trim() ?? "";
        var list = new List<Episode>();
        foreach (Match m in AudioItem().Matches(html))
        {
            var url = Regex.Unescape(m.Groups[3].Value);
            if (url.StartsWith("//")) url = "https:" + url;
            list.Add(new Episode(Regex.Unescape(m.Groups[1].Value), url));
        }
        var title = doc.QuerySelector("h1")?.TextContent;
        return new BookDetail(list, intro)
        {
            Title = title != null ? CleanTitle(Clean(title)).Title : null,
            Artist = list.Count > 0 ? Regex.Unescape(AudioItem().Match(html).Groups[2].Value) : null,
        };
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Direct;

    public IDictionary<string, string>? GetAudioHeaders(string audioUrl) =>
        audioUrl.Contains("xsmp3.com") ? new Dictionary<string, string> { ["Referer"] = Site } : null;

    [GeneratedRegex(@"xsmp3\.com/[a-z0-9-]+/[^/]+\.html$")]
    private static partial Regex ArticleUrl();

    [GeneratedRegex(@"name:\s*""((?:[^""\\]|\\.)*)""\s*,\s*artist:\s*""((?:[^""\\]|\\.)*)""\s*,\s*url:\s*""([^""]+)""")]
    private static partial Regex AudioItem();
}
