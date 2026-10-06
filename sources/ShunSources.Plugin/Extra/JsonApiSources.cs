using TingShu.Sdk;

namespace ShunSources;

/// <summary>博看有声（来自 sources_by_eprendre.jar）</summary>
public sealed class BoKanYouSheng : ShunSource
{
    private const string Instance = "25304";

    public override string Id => "c98a21452583434da5cfef8be16b71d6";
    public override string Name => "博看有声";
    public override string Url => "https://voicewk.bookan.com.cn/25303/index";
    public override string Description => "推荐指数:5星 ⭐⭐⭐⭐⭐\n图书馆数字资源，经典名著、人文社科类有声书。";
    public override bool IsMultipleEpisodePages => true;

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        // “图书”和“专辑”是两个接口，各自分页：每次两边都取第 page 页，总页数取较大的那个。
        // 接口最多只给前 100 条（再往后返回错误 40011），所以最多 5 页
        const int limit = 20, maxPage = 100 / limit;
        var books = new List<Book>();
        var totalPage = 1;
        if (page > maxPage) return new SearchResult(books, maxPage);
        foreach (var type in new[] { "book", "album" })
        {
            try
            {
                var json = await Host.GetJsonAsync(
                    $"https://es.bookan.com.cn/api/v3/voice/{type}?instanceId={Instance}&keyword={Enc(keywords)}&pageNum={page}&limitNum={limit}", ct: ct);
                var data = json["data"];
                totalPage = Math.Max(totalPage, Math.Min(maxPage, data.Int("last_page")));
                books.AddRange(data.Items("list").Select(i => new Book(i.Str("cover"), i.Str("id"), i.Str("name"))
                {
                    Status = i.Int("total") > 0 ? $"共 {i.Int("total")} 章" : "",
                    Intro = i.Str("intro"),
                }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Host.Log($"搜索 {type} 失败：{ex.Message}");
            }
        }
        return new SearchResult(books, Math.Max(totalPage, page));
    }

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        static CategoryTab T(string type, string title, int id) => new(title, $"https://api.bookan.com.cn/voice/{type}/list?instance_id={Instance}&page=1&category_id={id}&num=24");
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("图书", new[]
            {
                T("book", "经典必读", 1314), T("book", "国学经典", 1320), T("book", "文学文艺", 1306), T("book", "少年读物", 1305), T("book", "儿童文学", 1304),
                T("book", "心理哲学", 1310), T("book", "育儿心经", 1309), T("book", "家庭健康", 1311), T("book", "青春励志", 1307), T("book", "历史小说", 1312),
                T("book", "商业财经", 1315), T("book", "科技科普", 1313), T("book", "故事会", 1303), T("book", "红色岁月", 1316), T("book", "社会观察", 1318),
                T("book", "音乐戏曲", 1317), T("book", "相声评书", 1319),
            }),
            new CategoryMenu("专辑", new[]
            {
                T("album", "健康养生", 4), T("album", "休闲娱乐", 5), T("album", "财经科技", 6), T("album", "广播节目", 7), T("album", "人文社科", 8),
                T("album", "少儿学堂", 9), T("album", "文史军事", 10), T("album", "投资理财", 11), T("album", "亲子教育", 12), T("album", "时尚生活", 13),
                T("album", "汽车知识", 14), T("album", "发展创业", 15), T("album", "婚恋情感", 16), T("album", "自我提升", 17), T("album", "商业资讯", 18),
                T("album", "新闻热点", 19),
            }),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var data = (await Host.GetJsonAsync(url, ct: ct))["data"];
        var current = data.Int("current_page", defaultValue: 1);
        var total = data.Int("last_page", defaultValue: 1);
        var books = data.Items("list").Select(i => new Book(i.Str("cover"), i.Str("id"), i.Str("name")) { Status = $"共 {i.Int("total")} 章" }).ToList();
        var next = current < total ? System.Text.RegularExpressions.Regex.Replace(url, @"page=\d+", $"page={current + 1}") : null;
        return new CategoryPage(books, current, total, url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (!loadEpisodes) return new BookDetail(list);
        string Units(int p) => $"https://api.bookan.com.cn/voice/album/units?album_id={bookUrl}&page={p}&num=20&order=1";
        var data = (await Host.GetJsonAsync(Units(1), ct: ct))["data"];
        var total = data.Int("last_page", defaultValue: 1);
        list.AddRange(ParseUnits(data));
        if (loadFullPages)
        {
            for (var p = 2; p <= total; p++)
            {
                ct.ThrowIfCancellationRequested();
                Host.ReportProgress($"{p} / {total}");
                list.AddRange(ParseUnits((await Host.GetJsonAsync(Units(p), ct: ct))["data"]));
                await Task.Delay(Random.Shared.Next(100, 400), ct);
            }
            Host.ReportProgress(null);
        }
        return new BookDetail(list);
    }

    private static IEnumerable<Episode> ParseUnits(System.Text.Json.Nodes.JsonNode? data) =>
        data.Items("list").Select(i => new Episode(i.Str("title"), i.Str("file")));

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Direct;
}

/// <summary>云图有声（来自 sources_by_eprendre.jar）</summary>
public sealed class YunTuYouSheng : ShunSource
{
    private const string Api = "http://open-service.yuntuys.com/api/w_ys/book/";
    private const string Wechat = "wechat:07955551-706c-4259-9aa0-db4627dfca57";

    public override string Id => "ab0a5474cd6a40e3ba65045addad390a";
    public override string Name => "云图有声";
    public override string Url => "http://yuntuwechat.yuntuys.com/home";
    public override string Description => "推荐指数:5星 ⭐⭐⭐⭐⭐\n正版有声书平台，经典文学、历史、少儿等。";
    public override bool IsMultipleEpisodePages => true;

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var data = (await Host.GetJsonAsync($"{Api}search/{Wechat}/{Enc(keywords)}?pageSize=20&pageNum={page}", ct: ct))["data"];
        return new SearchResult(data.Items("list").Select(ParseBook).ToList(), Math.Max(1, data.Int("totalPage")));
    }

    private static Book ParseBook(System.Text.Json.Nodes.JsonNode i) =>
        new(i.Str("cover"), i.Str("bookId"), i.Str("bookName"), i.Str("authorName"), i.Str("anchorName"))
        {
            Status = $"共 {i.Int("chapters")} 章",
            Intro = i.Str("summary"),
        };

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        static CategoryMenu M(string title, params (string Name, int Id)[] tabs) =>
            new(title, tabs.Select(t => new CategoryTab(t.Name, $"{Api}getBookListByType/{Wechat}/{t.Id}?pageNum=1&pageSize=20")).ToList());
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            M("特色", ("四史专栏", 585), ("远读重洋", 571), ("听见真知", 124), ("豆瓣高分", 125), ("广播剧", 126), ("影视同期", 127), ("云图学院", 421)),
            M("经典文学", ("世界名著", 131), ("中国文学", 132), ("国学经典", 134), ("外国文学", 133), ("诗词散文", 135), ("人物传记", 136)),
            M("畅销小说", ("国风古韵", 137), ("青春校园", 138), ("科学幻想", 139), ("官场商战", 140), ("军事谍战", 141), ("悬疑推理", 142), ("现代都市", 143), ("怪奇物语", 144), ("侠义江湖", 335)),
            M("职场财经", ("创业学院", 148), ("职场指南", 149), ("商界大咖", 150), ("金融理财", 151)),
            M("少儿教育", ("儿童文学", 152), ("童话名著", 153), ("国学启蒙", 154), ("儿歌故事", 155), ("百科知识", 156), ("亲子教育", 157)),
            M("文化历史", ("民俗文化", 161), ("世界之窗", 163), ("哲学思想", 164), ("古代历史", 165), ("近现代史", 166), ("世界历史", 167), ("传奇史话", 168)),
            M("军事", ("军事纪实", 173), ("战争烽火", 174), ("革命先驱", 176), ("政治领袖", 177)),
            M("生活", ("养生保健", 181), ("养颜减肥", 182), ("食疗课堂", 183), ("孕产育儿", 184), ("心理健康", 185), ("婚恋家庭", 186), ("心灵励志", 187), ("生活百科", 188), ("娱乐休闲", 189)),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var data = (await Host.GetJsonAsync(url, ct: ct))["data"];
        var current = data.Int("pageNumber", defaultValue: 1);
        var total = Math.Max(current, data.Int("totalPage", defaultValue: 1));
        var next = current < total ? System.Text.RegularExpressions.Regex.Replace(url, @"pageNum=\d+", $"pageNum={current + 1}") : null;
        return new CategoryPage(data.Items("list").Select(ParseBook).ToList(), current, total, url, next);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (!loadEpisodes) return new BookDetail(list);
        string Chapters(int p) => $"{Api}getChapters/{Wechat}/{bookUrl}/true/asc?pageSize=200&pageNum={p}";
        var query = (await Host.GetJsonAsync(Chapters(1), ct: ct)).At("data.pageQuery");
        var total = query.Int("totalPage", defaultValue: 1);
        list.AddRange(query.Items("list").Select(i => new Episode(i.Str("name"), i.Str("audioUrl"))));
        if (loadFullPages)
        {
            for (var p = 2; p <= total; p++)
            {
                ct.ThrowIfCancellationRequested();
                Host.ReportProgress($"{p} / {total}");
                var q = (await Host.GetJsonAsync(Chapters(p), ct: ct)).At("data.pageQuery");
                list.AddRange(q.Items("list").Select(i => new Episode(i.Str("name"), i.Str("audioUrl"))));
                await Task.Delay(Random.Shared.Next(100, 400), ct);
            }
            Host.ReportProgress(null);
        }
        return new BookDetail(list);
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Direct;
}
