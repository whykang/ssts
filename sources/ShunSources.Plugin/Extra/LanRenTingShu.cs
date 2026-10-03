using System.Text.Json.Nodes;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 懒人听书（m.lrts.me）—— 从安卓版 LanRenTingShu.kt 移植。
/// 演示：JSON 接口、分页章节、自定义音频提取（免费章节走接口，付费章节走 WebView 嗅探）、登录。
/// </summary>
public sealed class LanRenTingShu : SourceBase, ILoginSource
{
    private const int EpisodePageSize = 50; // 接口目前最多支持 50
    private const int CategoryPageSize = 20;

    // 分类第一页会返回该分类下全部书籍 ID，后续页按 ID 列表请求
    private readonly Dictionary<string, List<long>> _categoryBookIds = new();

    public override string Id => "4a3ed84e5cf841609ed4d7f790fc7fbf";
    public override string Name => "懒人听书";
    public override string Url => "https://m.lrts.me";
    public override string Description => "正版源，免费书籍较少。带 [VIP] / [精品] 的书需要登录对应账号后才能收听。";
    public override bool IsMultipleEpisodePages => true;

    public string LoginUrl => "https://m.lrts.me/user";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var url = $"https://m.lrts.me/ajax/search?keyWord={Uri.EscapeDataString(keywords)}&pageSize=40&pageNum={page}&searchOption=1";
        var json = await Host.GetJsonAsync(url, ct: ct);
        var result = json.At("data.bookResult");
        var totalPage = (int)Math.Ceiling(result.Int("count") / 40.0);
        var books = result.Items("list").Select(ParseBook).ToList();
        return new SearchResult(books, Math.Max(totalPage, 1));
    }

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        static CategoryTab Tab(string title, int id) =>
            new(title, $"https://m.lrts.me/ajax/getResourceList?dsize={CategoryPageSize}&entityId={id}&entityType=1&pageNum=1&showFilters=1");

        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("有声小说", new[]
            {
                Tab("玄幻奇幻", 11), Tab("都市传说", 8), Tab("穿越架空", 3109), Tab("武侠仙侠", 14), Tab("青春校园", 3106),
                Tab("历史幻想", 12), Tab("科幻空间", 3021), Tab("网游竞技", 9042), Tab("热血军事", 9041), Tab("官场商战", 44),
            }),
            new CategoryMenu("人文", new[]
            {
                Tab("心理百科", 9045), Tab("科学科普", 9046), Tab("人物传记", 17), Tab("纪实传奇", 3063), Tab("哲学宗教", 1026),
                Tab("文艺文化", 9044), Tab("公开课", 109),
            }),
            new CategoryMenu("财经", new[]
            {
                Tab("投资理财", 3059), Tab("股市", 3058), Tab("商业智慧", 3057), Tab("管理营销", 16), Tab("创业", 9048),
            }),
            new CategoryMenu("儿童", new[]
            {
                Tab("益智故事", 63), Tab("儿童文学", 3027), Tab("国学启蒙", 9031), Tab("卡通动画", 9029), Tab("少儿名著", 9245),
                Tab("少儿科普", 64), Tab("少儿英语", 68),
            }),
            new CategoryMenu("曲艺戏曲", new[]
            {
                Tab("戏曲名家", 9060), Tab("豫剧", 95), Tab("京剧", 89), Tab("黄梅戏", 93), Tab("越剧", 90), Tab("鼓书琴书", 67),
            }),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var query = ParseQuery(url);
        var entityId = query.GetValueOrDefault("entityId", "");
        var json = await Host.GetJsonAsync(url, ct: ct);

        var useAlbums = json.Int("albumCount") > 0;
        var books = json.Items(useAlbums ? "albums" : "books").Select(ParseBook).ToList();

        int currentPage;
        if (!query.TryGetValue("bookIds", out var idsParam))
        {
            currentPage = 1;
            _categoryBookIds[entityId] = json.Items(useAlbums ? "albumIds" : "bookIds").Select(n => n.Long()).ToList();
        }
        else
        {
            currentPage = int.TryParse(query.GetValueOrDefault("page"), out var p) ? p : 2;
        }

        string? nextUrl = null;
        if (_categoryBookIds.TryGetValue(entityId, out var ids))
        {
            var from = currentPage * CategoryPageSize;
            if (from < ids.Count)
            {
                var slice = ids.Skip(from).Take(CategoryPageSize);
                nextUrl = $"https://m.lrts.me/ajax/getResourceList?dsize={CategoryPageSize}&entityId={entityId}&entityType=0&pageNum=0&showFilters=0" +
                          $"&page={currentPage + 1}&bookIds=[{string.Join(",", slice)}]";
            }
        }
        var totalPage = nextUrl == null ? currentPage : currentPage + 1;
        return new CategoryPage(books, currentPage, totalPage, url, nextUrl);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var bookId = bookUrl.Split('?').Last();
        var episodes = new List<Episode>();

        if (loadEpisodes)
        {
            var first = await GetMenuAsync(bookId, 1, ct);
            var pageCount = (int)Math.Ceiling(first.Int("sections") / (double)EpisodePageSize);
            episodes.AddRange(ParseEpisodes(first, bookId, 0));

            if (loadFullPages)
            {
                for (var page = 2; page <= pageCount; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    Host.ReportProgress($"{page} / {pageCount}");
                    var json = await GetMenuAsync(bookId, page, ct);
                    episodes.AddRange(ParseEpisodes(json, bookId, episodes.Count));
                    await Task.Delay(Random.Shared.Next(100, 400), ct);
                }
                Host.ReportProgress(null);
            }
        }

        var info = await Host.GetJsonAsync($"https://m.lrts.me/ajax/getBookInfo?id={bookId}", ct: ct);
        return new BookDetail(episodes, info.Str("extraInfos.0.content"))
        {
            EpisodesCount = info.Int("sections"),
            Artist = info.Str("announcer"),
            Author = info.Str("author"),
            CoverUrl = info.Str("bestCover"),
        };
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom(async (url, ct) =>
    {
        if (url.Contains("getPlayPath"))
        {
            var json = await Host.GetJsonAsync(url, new RequestOptions { UseWebViewCookies = true }, ct);
            var path = json.Str("list.0.path");
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException(json.Str("msg") is { Length: > 0 } msg ? msg : "无法获取音频地址");
            return path;
        }
        // 付费章节：打开播放页嗅探（需要先在插件页面登录）
        return await Host.SniffMediaAsync(url, ct: ct);
    });

    private async Task<JsonNode> GetMenuAsync(string bookId, int page, CancellationToken ct)
    {
        var url = $"https://m.lrts.me/ajax/getBookMenu?bookId={bookId}&pageNum={page}&pageSize={EpisodePageSize}&sortType=0";
        var json = await Host.GetJsonAsync(url, ct: ct);
        // 出错时返回 {"status":"n1006","msg":"服务器繁忙"}
        if (json["list"] == null && json.Str("msg") is { Length: > 0 } msg) throw new InvalidOperationException(msg);
        return json;
    }

    private static IEnumerable<Episode> ParseEpisodes(JsonNode json, string bookId, int offset)
    {
        var index = offset;
        foreach (var track in json.Items("list"))
        {
            var isFree = track.Int("payType") == 0;
            var url = isFree
                ? $"https://m.lrts.me/ajax/getPlayPath?entityId={bookId}&entityType=3&opType=1&sections=[{track.Int("section")}]&type=0"
                : $"https://m.lrts.me/player?index={index}&entityType=3&sonId={track.Str("id")}&id={bookId}";
            index++;
            yield return new Episode(track.Str("name"), url) { IsFree = isFree };
        }
    }

    private Book ParseBook(JsonNode node)
    {
        var cover = node.Str("cover");
        if (cover.Length > 0 && !cover.Contains("180"))
        {
            var dot = cover.LastIndexOf('.');
            if (dot > 0) cover = $"{cover[..dot]}_180x254{cover[dot..]}";
        }
        var title = node.Str("name");
        var tags = node.Str("tags");
        if (tags.Contains("VIP")) title = "[VIP] " + title;
        else if (tags.Contains("精品")) title = "[精品] " + title;

        var finished = node.Int("state") == 2;
        var sections = node.Int("sections");
        var intro = node.Str("recReason");
        if (intro.Length == 0 || intro == "null") intro = node.Str("desc");

        return new Book(cover, $"https://m.lrts.me?{node.Str("id")}", title, node.Str("author"), node.Str("announcer"))
        {
            Intro = intro,
            Status = (finished ? "完本" : "连载") + (sections > 0 ? $" · {sections} 集" : ""),
            IsCompleted = finished,
        };
    }

    private static Dictionary<string, string> ParseQuery(string url)
    {
        var q = url.Contains('?') ? url[(url.IndexOf('?') + 1)..] : "";
        return q.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => g.First().Length > 1 ? Uri.UnescapeDataString(g.First()[1]) : "");
    }
}
