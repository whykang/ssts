using TingShu.Sdk;

namespace ShunSources;

/// <summary>单田芳评书网（来自 sources_by_eprendre.jar）</summary>
public sealed class PingShu5 : ShunSource
{
    public override string Id => "accbef4f823c4cc9ae76ba24688c67f6";
    public override string Name => "单田芳评书网";
    public override string Url => "http://www.pingshu5.net/";
    public override string Description => "推荐指数:3星 ⭐⭐⭐\n单田芳、袁阔成、刘兰芳、连丽如评书。只有分类，不支持搜索。";
    public override string Group => "评书";
    public override bool IsSearchable => false;

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("评书分类", new[]
            {
                new CategoryTab("单田芳", "http://www.pingshu5.net/pbook/"), new CategoryTab("袁阔成", "http://www.pingshu5.net/ykc/"),
                new CategoryTab("刘兰芳", "http://www.pingshu5.net/llf/"), new CategoryTab("连丽如", "http://www.pingshu5.net/llr/"),
            }),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var books = new List<Book>();
        foreach (var li in doc.Select(".pop-books2 > ul > li"))
        {
            var a = li.SelectFirst(".caption > a");
            if (a == null) continue;
            books.Add(new Book(li.SelectFirst("img").AbsUrl("src"), li.SelectFirst("a").AbsUrl("href"), a.Text())
            {
                Intro = li.SelectFirst(".caption > span").Text(),
            });
        }
        return new CategoryPage(books, 1, 1, url, null);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await GetDoc(bookUrl, true, ct);
        return new BookDetail(doc.Select(".book-list > ul > li > a").Select(a => new Episode(a.Text(), a.AbsUrl("href"))).ToList());
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Html(doc =>
        doc.SelectFirst("audio > source")?.AbsUrl("src") is { Length: > 0 } s ? s : doc.SelectFirst("audio").AbsUrl("src"), desktop: true);
}

/// <summary>声音巴士（来自 sources_by_eprendre.jar）</summary>
public sealed class VBus : ShunSource
{
    public override string Id => "405d26b44ad24b25a450ede64bac682f";
    public override string Name => "声音巴士";
    public override string Url => "https://vbus.cc/";
    public override string Description => "推荐指数:2星 ⭐⭐\n一个无障碍交流平台，一群热爱声音的人。只有分类，不支持搜索。";
    public override string Group => "播客";
    public override bool IsSearchable => false;

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("声音巴士", new[]
            {
                new CategoryTab("推荐", "https://vbus.cc/recommend/1"), new CategoryTab("最新", "https://vbus.cc/new/1"),
                new CategoryTab("最热", "https://vbus.cc/hot/1"),
            }),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, false, ct);
        var next = doc.Select(".page-item > a").FirstOrDefault(a => a.Text().Contains("下一页"))?.AbsUrl("href");
        var current = RegexInt(url, @"/(\d+)$");
        var books = new List<Book>();
        foreach (var li in doc.Select(".program-list > li"))
        {
            var a = li.SelectFirst("h3 > a");
            var meta = li.SelectFirst(".program-meta");
            if (a == null) continue;
            var spans = meta?.Select("span").ToList();
            books.Add(new Book("", a.AbsUrl("href"), a.Text(),
                string.Join(",", meta?.Children.Where(c => c.LocalName == "a").Select(c => c.Text()) ?? Array.Empty<string>()))
            {
                Status = spans?.LastOrDefault().Text() ?? "",
            });
        }
        return new CategoryPage(books, current, next != null ? current + 1 : current, url, next);
    }

    /// <summary>每个节目只有一段音频</summary>
    public override Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct) =>
        Task.FromResult(new BookDetail(new List<Episode> { new("音频", bookUrl) }));

    // 播放页的 audio 指向 /program/get-source/{id}，会再跳转到真实音频（可能从 https 跳到 http）
    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom(async (url, ct) =>
    {
        var doc = await GetDoc(url.Replace("://www.vbus.cc", "://vbus.cc"), false, ct);
        var src = doc.SelectFirst("audio > source")?.AbsUrl("src") is { Length: > 0 } s ? s : doc.SelectFirst("audio").AbsUrl("src");
        if (src.Length == 0) throw new InvalidOperationException("没有找到音频");
        return await ResolveRedirects(src, ct);
    });
}

/// <summary>
/// 米听书 ting78.com（来自 my_sound_01.jar）—— 与乐听吧是同一套网站程序
/// </summary>
public sealed class Mting : PageBarCmsSource
{
    public override string Id => "63a4f98615394edea416ba5fd15b569d";
    public override string Name => "米听书";
    protected override string Site => "https://www.ting78.com/";
    public override string Description => "搜索需要人机验证：在搜索页点击该源旁的“验证”，完成后会自动重新搜索。";

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var names = new[] { "玄幻", "言情", "都市", "恐怖", "惊悚", "推理", "武侠", "历史", "军事", "穿越", "科幻", "网游", "评书", "戏曲", "笑话", "儿童", "财经", "诗歌", "文学", "粤语", "经典", "相声小品", "百家讲坛" };
        var ids = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 19, 20, 21, 22, 23, 24 };
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("有声小说", names.Select((n, i) => new CategoryTab(n, $"{Site}books/{ids[i]}.html")).ToList()),
        };
        return Task.FromResult(menus);
    }
}
