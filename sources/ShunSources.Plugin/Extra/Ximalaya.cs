using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 喜马拉雅。搜索、分类、专辑和章节用公开接口，不登录也能浏览；播放地址是加密的（AES-ECB）。
/// 免费内容不登录就能听；会员和已购买的内容，在插件页点“登录”用自己的账号登录后才能听。
/// </summary>
public sealed partial class Ximalaya : ShunSource, ILoginSource
{
    private const string Web = "https://www.ximalaya.com";
    private const string Key = "aaad3e4fd540b0f79dca95606e72bf93"; // 播放地址的解密密钥（网页播放器里公开的）
    private const int PageSize = 200;

    // 发现页显示哪些频道（网站的频道很多，只挑和听书相关的）
    private static readonly string[] Channels = { "youshengshu", "guangbojv", "xiangsheng", "ertong", "lishi", "renwen", "xiqu", "qinggan" };

    public override string Id => "6e2b9c4d8a1f4d37b5c0e73a9f1d2b64";
    public override string Name => "喜马拉雅";
    public override string Url => Web + "/";
    public override string Description => "喜马拉雅的有声书、广播剧、相声评书等。免费内容可以直接听；会员或已购买的内容，先点“登录”用自己的账号登录。";
    public override bool IsMultipleEpisodePages => true;

    public string LoginUrl => "https://passport.ximalaya.com/page/web/login";
    public bool LoginDesktop => true;

    /// <summary>网页版接口：带上登录后的 Cookie（没登录也能用）</summary>
    private Task<JsonNode> WebAsync(string url, CancellationToken ct) => Host.GetJsonAsync(url, new RequestOptions
    {
        Desktop = true,
        Headers = new Dictionary<string, string> { ["Referer"] = Web + "/" },
        UseWebViewCookies = true,
    }, ct);

    private static string Cover(string u)
    {
        if (u.Length == 0) return "";
        if (u.StartsWith("//")) u = "https:" + u;
        if (u.StartsWith("http:")) u = "https:" + u[5..];
        // 去掉地址后面的图片处理参数，换成固定的小尺寸
        return u.Split('!')[0] + "!op_type=3&columns=290&rows=290";
    }

    private static string AlbumUrl(string id) => $"{Web}/album/{id}";
    private static string Finished(JsonNode? v) => v?.ToString() == "2" ? "完结" : "连载";
    private static bool True(JsonNode? v) => v?.ToString() is "true" or "1";

    [GeneratedRegex(@"album/(\d+)")]
    private static partial Regex AlbumId();

    [GeneratedRegex(@"^xm-cat:([^:]*):([^#]*)#(\d+)$")]
    private static partial Regex CategoryUrl();

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var json = await Host.GetJsonAsync(
            $"https://search.ximalaya.com/front/v1?core=album&kw={Enc(keywords)}&page={page}&rows=20&spellchecker=true&condition=relation&device=android&version=9.0.75", ct: ct);
        var r = json["response"];
        var books = r.Items("docs").Select(d => new Book(Cover(d.Str("cover_path")), AlbumUrl(d.Str("id")), d.Str("title"), "", d.Str("nickname"))
        {
            Intro = d.Str("custom_title") is { Length: > 0 } t ? t : d.Str("intro"),
            Status = $"{Finished(d["is_finished"])} · {d.Int("tracks")} 集{(True(d["is_paid"]) ? " · 付费" : "")}",
            IsCompleted = d["is_finished"]?.ToString() == "2",
        }).ToList();
        return new SearchResult(books, Math.Max(page, r.Int("totalPage", page)));
    }

    /// <summary>每个频道一组，组里是它的子分类</summary>
    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var groups = (await WebAsync(Web + "/revision/category/allCategoryInfo", ct)).Items("data");
        var found = groups.SelectMany(g => g.Items("categories")).GroupBy(c => c.Str("pinyin")).ToDictionary(g => g.Key, g => g.First());
        var menus = new List<CategoryMenu>();
        foreach (var pinyin in Channels)
        {
            if (!found.TryGetValue(pinyin, out var c)) continue;
            var tabs = new List<CategoryTab> { new("全部", $"xm-cat:{pinyin}:#1") };
            foreach (var s in c.Items("subcategories"))
                if (s.Str("code").Length > 0) tabs.Add(new CategoryTab(s.Str("displayValue"), $"xm-cat:{pinyin}:{s.Str("code")}#1"));
            menus.Add(new CategoryMenu(c.Str("displayName"), tabs));
        }
        return menus;
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var m = CategoryUrl().Match(url);
        var page = int.Parse(m.Groups[3].Value);
        const int perPage = 30;
        var json = await WebAsync(
            $"{Web}/revision/category/queryCategoryPageAlbums?category={m.Groups[1].Value}&subcategory={m.Groups[2].Value}&meta=&sort=0&page={page}&perPage={perPage}", ct);
        var data = json["data"];
        var books = data.Items("albums").Select(a => new Book(Cover(a.Str("coverPath")), AlbumUrl(a.Str("albumId")), a.Str("title"), "", a.Str("anchorName"))
        {
            Status = $"{Finished(a["isFinished"])} · {a.Int("trackCount")} 集{(True(a["isPaid"]) ? " · 付费" : "")}",
            IsCompleted = a["isFinished"]?.ToString() == "2",
        }).ToList();
        // 网站最多能翻到 50 页左右，再往后是空的
        var total = Math.Min(50, (int)Math.Ceiling(data.Int("total") / (double)perPage));
        var next = books.Count > 0 && page < total ? $"xm-cat:{m.Groups[1].Value}:{m.Groups[2].Value}#{page + 1}" : null;
        return new CategoryPage(books, page, Math.Max(total, page), url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var id = AlbumId().Match(bookUrl).Groups[1].Value;
        var episodes = new List<Episode>();
        var detail = new BookDetail(episodes);
        try
        {
            var info = (await WebAsync($"{Web}/revision/album/v1/simple?albumId={id}", ct)).At("data.albumPageMainInfo");
            detail.Title = info.Str("albumTitle") is { Length: > 0 } t ? t : null;
            detail.CoverUrl = Cover(info.Str("cover")) is { Length: > 0 } c ? c : null;
            detail.Artist = info.Str("anchorName") is { Length: > 0 } a ? a : null;
            detail.Intro = info.Str("richIntro") is { Length: > 0 } rich ? rich : info.Str("shortIntro");
            detail.Status = Finished(info?["isFinished"]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Host.Log("取专辑信息失败：" + ex.Message);
        }
        if (!loadEpisodes) return detail;

        try
        {
            for (var page = 1; page <= 500; page++)
            {
                // 登录后 isAuthorized 才准确，所以带上 Cookie
                var json = await Host.GetJsonAsync(
                    $"https://mobile.ximalaya.com/mobile/v1/album/track?albumId={id}&pageId={page}&pageSize={PageSize}&isAsc=true&device=android",
                    new RequestOptions { UseWebViewCookies = true }, ct);
                // 专辑被下架等情况：接口不给章节，只给一句原因，原样告诉用户
                if (json.Int("ret") != 0 && episodes.Count == 0)
                    throw new InvalidOperationException("喜马拉雅：" + (json.Str("msg") is { Length: > 0 } msg ? msg : $"获取章节失败（{json.Str("ret")}）"));
                var data = json["data"];
                foreach (var t in data.Items("list"))
                {
                    // 付费专辑里也有可以免费试听的章节；已经买过 / 是会员时 isAuthorized 为 true
                    episodes.Add(new Episode(t.Str("title"), "xm:" + t.Str("trackId"))
                    {
                        IsFree = !True(t["isPaid"]) || True(t["isFree"]) || True(t["isAuthorized"]),
                    });
                }
                var max = data.Int("maxPageId", 1);
                if (!loadFullPages || page >= max) break;
                Host.ReportProgress($"{page + 1} / {max}");
                await Task.Delay(Random.Shared.Next(150, 400), ct);
            }
        }
        finally
        {
            Host.ReportProgress(null);
        }
        return detail;
    }

    /// <summary>播放地址带时效签名，每次播放时现取；登录后请求会带上账号的 Cookie</summary>
    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom(async (url, ct) =>
    {
        var id = url.StartsWith("xm:") ? url[3..] : url;
        var json = await WebAsync(
            $"{Web}/mobile-playpage/track/v3/baseInfo/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}?device=web&trackId={id}&trackQualityLevel=1", ct);
        var list = json.Items("trackInfo.playUrlList").ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("这一集需要会员或购买后才能听：到插件页找到“喜马拉雅”点“登录”，用自己的账号登录后再试" +
                                                (json.Str("msg") is { Length: > 0 } msg ? $"（{msg}）" : ""));
        // 优先 64k 的 m4a，其次 mp3
        string[] order = { "M4A_64", "MP3_64", "M4A_24", "MP3_32" };
        var pick = order.Select(type => list.FirstOrDefault(u => u.Str("type") == type)).FirstOrDefault(u => u != null) ?? list[0];
        var audio = Decrypt(pick.Str("url"));
        if (!audio.StartsWith("http")) throw new InvalidOperationException("喜马拉雅的播放地址解密失败，可能是网站改了加密方式");
        return audio;
    });

    /// <summary>AES-ECB（PKCS7 填充）解密：data 是 base64（普通或 URL 安全的都行），返回 UTF-8 文本；失败返回空字符串</summary>
    private static string Decrypt(string data)
    {
        try
        {
            var b64 = data.Trim().Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var aes = Aes.Create();
            aes.Key = Convert.FromHexString(Key);
            return Encoding.UTF8.GetString(aes.DecryptEcb(Convert.FromBase64String(b64), PaddingMode.PKCS7));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return "";
        }
    }
}
