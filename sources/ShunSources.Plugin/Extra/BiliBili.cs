using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 哔哩哔哩（来自 sources_by_eprendre.jar）：搜索视频，按分 P 作为章节，播放 HTML5 版 mp4 的音轨
/// </summary>
public sealed class BiliBili : ShunSource, IAudioHeaders
{
    private const string Api = "https://api.bilibili.com/x/";

    // B 站接口需要一个 buvid3 Cookie（随机生成即可，与原版相同）
    private static readonly string Cookie = $"buvid3={Guid.NewGuid().ToString().ToUpperInvariant()}infoc";

    public override string Id => "c893546d95f84db194046bd8de5dbcbb";
    public override string Name => "哔哩哔哩";
    public override string Url => "https://m.bilibili.com";
    public override string Description => "推荐指数:5星 ⭐⭐⭐⭐⭐\nB 站有很多意想不到的有声书。播放的是视频的声音，多 P 视频每一 P 为一集。";
    public override string Group => "听书";
    protected override string CoverDomain => "hdslb.com";
    protected override string Referer => "https://www.bilibili.com/";

    private Task<JsonNode> Get(string url, CancellationToken ct) => Host.GetJsonAsync(url, new RequestOptions
    {
        Desktop = true,
        Headers = new Dictionary<string, string> { ["Cookie"] = Cookie, ["Referer"] = "https://www.bilibili.com/" },
    }, ct);

    private static string ApiError(JsonNode json) => $"B 站接口返回错误：{json.Str("message")}（{json.Str("code")}）";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var json = await Get($"{Api}web-interface/search/type?search_type=video&keyword={Enc(keywords)}&page={page}", ct);
        if (json.Int("code") != 0) throw new InvalidOperationException(ApiError(json));
        var data = json["data"];
        return new SearchResult(data.Items("result").Select(ParseVideo).ToList(), Math.Max(1, data.Int("numPages")));
    }

    private static Book ParseVideo(JsonNode v)
    {
        var pic = v.Str("pic");
        if (pic.StartsWith("//")) pic = "https:" + pic;
        var title = WebUtility.HtmlDecode(Regex.Replace(v.Str("title"), "<[^>]+>", ""));
        return new Book(pic, "https://m.bilibili.com/video/" + v.Str("bvid"), title, "", v.Str("author"))
        {
            Status = $"播放 {FormatCount(v.Long("play"))} · {v.Str("duration")}",
            Intro = v.Str("description"),
        };
    }

    private static string FormatCount(long n) => n >= 10000 ? $"{n / 10000.0:0.#}万" : n.ToString();

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        // 与原版相同：分类即按关键词搜索
        static CategoryTab T(string title, string keyword) => new(title, $"bili-search:{keyword}#1");
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("推荐", new[]
            {
                T("有声小说", "有声小说"), T("广播剧", "广播剧"), T("评书", "评书"), T("相声", "相声"), T("有声漫画", "有声漫画"),
                T("英文有声书", "audiobooks"), T("经典老歌", "经典老歌"), T("音乐推荐", "音乐推荐"), T("同人音声", "同人音声"),
            }),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var m = Regex.Match(url, @"^bili-search:(.+)#(\d+)$");
        var keyword = m.Groups[1].Value;
        var page = int.Parse(m.Groups[2].Value);
        var result = await SearchAsync(keyword, page, ct);
        var next = page < result.TotalPage ? $"bili-search:{keyword}#{page + 1}" : null;
        return new CategoryPage(result.Books, page, result.TotalPage, url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var bvid = bookUrl.TrimEnd('/').Split('/').Last().Split('?')[0];
        var json = await Get($"{Api}web-interface/view?bvid={bvid}", ct);
        if (json.Int("code") != 0) throw new InvalidOperationException(ApiError(json));
        var data = json["data"];
        var aid = data.Str("aid");
        var pages = data.Items("pages").ToList();
        var list = pages.Select(p =>
        {
            var part = p.Str("part");
            var title = pages.Count == 1 ? data.Str("title") : (part.Length > 0 ? part : $"P{p.Int("page")}");
            return new Episode(title, $"bili:{aid}:{p.Str("cid")}");
        }).ToList();

        var pic = data.Str("pic");
        return new BookDetail(list, data.Str("desc"))
        {
            Title = data.Str("title"),
            Artist = data.Str("owner.name"),
            CoverUrl = pic.StartsWith("//") ? "https:" + pic : pic,
        };
    }

    /// <summary>播放地址带时效签名，每次播放时现取</summary>
    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom(async (url, ct) =>
    {
        var parts = url.Split(':');
        if (parts.Length != 3 || parts[0] != "bili") throw new InvalidOperationException("章节地址已过期，请刷新章节列表");
        var json = await Get($"{Api}player/playurl?avid={parts[1]}&cid={parts[2]}&platform=html5&otype=json&qn=16&type=mp4&html5=1", ct);
        if (json.Int("code") != 0) throw new InvalidOperationException(ApiError(json));
        var audio = json.Str("data.durl.0.url");
        return audio.Length > 0 ? audio : throw new InvalidOperationException("没有获取到播放地址（可能是大会员或付费内容）");
    });

    public IDictionary<string, string>? GetAudioHeaders(string audioUrl) =>
        audioUrl.Contains("bilivideo") || audioUrl.Contains("akamaized")
            ? new Dictionary<string, string> { ["Referer"] = "https://www.bilibili.com/" }
            : null;
}
