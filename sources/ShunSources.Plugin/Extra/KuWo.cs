using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>酷我畅听（来自 sources_by_eprendre.jar）</summary>
public sealed class KuWo : ShunSource
{
    private const string AlbumInfo = "https://search.kuwo.cn/r.s?stype=albuminfo&loginUid=0&loginSid=null&prod=kwplayer_ar_9.1.7.0&bkprod=kwbook_ar_9.1.7.0" +
                                     "&source=kwplayer_ar_9.1.7.0_t18.apk&bksource=kwbook_ar_9.1.7.0_t18.apk&corp=kuwo&show_copyright_off=1" +
                                     "&vipver=MUSIC_8.2.0.0_BCS17&mobi=1&sortby=3&iskwbook=1";

    public override string Id => "502efedf0613460a9967d9e86ce2b24c";
    public override string Name => "酷我畅听";
    public override string Url => "https://kuwo.cn/downtingshu";
    public override string Description => "推荐指数:5星 ⭐⭐⭐⭐⭐\n酷我音乐的听书频道，部分付费内容只能试听或无法播放。";
    public override string Group => "听书";
    public override bool IsMultipleEpisodePages => true;

    private static string BookUrl(string albumId) => $"{AlbumInfo}&albumid={albumId}&pn=0&rn=200";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var json = await Host.GetJsonAsync(
            $"http://tingshu.kuwo.cn/tingshu/api/search/Search?rn=10&type=album&version=8.5.6.1&wd={Enc(keywords)}&pn={page}&kweexVersion=1.0.2", ct: ct);
        var data = json["data"];
        var books = data.Items("data").Select(ParseAlbum).ToList();
        return new SearchResult(books, Math.Max(1, (int)Math.Ceiling(data.Int("total") / 10.0)));
    }

    private static Book ParseAlbum(JsonNode item) =>
        new(item.Str("coverImg"), BookUrl(item.Str("albumId")), item.Str("albumName"), "", item.Str("artistName"))
        {
            Status = $"共 {item.Int("songTotal")} 章",
            Intro = item.Str("title"),
        };

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        static CategoryTab C(string title, int classify, int category) => new(title,
            $"http://tingshu.kuwo.cn/tingshu/api/filter/albums?sortType=tsScore&classifyId={classify}&rn=20&categoryId={category}&pn=1&kweexVersion=1.0.2");
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("小说", new[]
            {
                new CategoryTab("免费排行", "http://tingshu.kuwo.cn/tingshu/api/page/boutique/getBoutiqueData?pt=2&rn=100&version=8.5.6.1&pn=1&kweexVersion=1.0.2"),
                C("都市传说", 42, 2), C("玄幻奇幻", 44, 2), C("悬疑推理", 45, 2), C("现代言情", 41, 2), C("武侠仙侠", 48, 2), C("穿越架空", 52, 2),
                C("经典小说", 64, 2), C("青春校园", 55, 2), C("历史军事", 56, 2), C("科幻竞技", 57, 2), C("古代言情", 207, 2),
            }),
            new CategoryMenu("成长", new[]
            {
                C("能力提升", 82, 4), C("人文艺术", 77, 4), C("国学文化", 78, 4), C("成功法则", 79, 4), C("外语精通", 76, 4), C("养生健康", 81, 4), C("酷我读书", 211, 4),
            }),
            new CategoryMenu("人文历史", new[]
            {
                C("国学经典", 117, 9), C("历史小说", 181, 9), C("纪实档案", 118, 9), C("历史传奇", 119, 9), C("人物传奇", 120, 9), C("文化讲堂", 121, 9), C("百家讲坛", 212, 9),
            }),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var page = RegexInt(url, @"pn=(\d+)");
        var data = (await Host.GetJsonAsync(url, ct: ct))["data"];
        int total;
        List<Book> books;
        if (data?["topDatas"] is JsonArray tops)
        {
            total = (int)Math.Ceiling(data.Int("pageInfo.total") / 100.0);
            books = tops.Where(t => t != null).Select(t => t!["albums"]).Where(a => a != null).Select(a =>
                new Book(a.Str("img"), BookUrl(a.Str("albumId")), a.Str("name"))
                {
                    Status = $"共 {a.Int("songTotal")} 章",
                    Intro = a.Str("title"),
                }).ToList();
        }
        else
        {
            total = (int)Math.Ceiling(data.Int("total") / 20.0);
            books = data.Items("data").Select(ParseAlbum).ToList();
        }
        var next = page < total ? Regex.Replace(url, @"pn=\d+", $"pn={page + 1}") : null;
        return new CategoryPage(books, page, Math.Max(total, page), url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (!loadEpisodes) return new BookDetail(list);
        var albumId = Regex.Match(bookUrl, @"albumid=(\d+)").Groups[1].Value;
        for (var page = 0; page < 100; page++)
        {
            ct.ThrowIfCancellationRequested();
            var json = await Host.GetJsonAsync($"{AlbumInfo}&albumid={albumId}&pn={page}&rn=200", ct: ct);
            var items = json.Items("musiclist").ToList();
            if (items.Count == 0) break;
            list.AddRange(items.Select(i => new Episode(i.Str("name"), "kuwo:" + i.Str("musicrid"))));
            if (!loadFullPages || items.Count < 200) break;
            Host.ReportProgress($"第 {page + 2} 页");
            await Task.Delay(Random.Shared.Next(200, 600), ct);
        }
        Host.ReportProgress(null);
        return new BookDetail(list);
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom(async (url, ct) =>
    {
        var rid = url.Replace("kuwo:", "").Replace("MUSIC_", "");
        var json = await Host.GetJsonAsync(
            $"https://mobi.kuwo.cn/mobi.s?f=web&source=kwplayercar_ar_6.0.0.9_B_jiakong_vh.apk&from=PC&type=convert_url_with_sign&br=128kmp3&rid={rid}",
            RequestOptions.Pc, ct);
        var audio = json.Str("data.url");
        return audio.Length > 0 ? audio : throw new InvalidOperationException("没有获取到播放地址（可能是付费内容）");
    });
}
