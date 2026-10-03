using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 听中国（手机版）m.i275.com —— 网站已改版为“275听书网”，按新版页面结构重写
/// </summary>
public sealed class Tingchina2 : ShunSource, ILoginSource
{
    public override string Id => "966475a6c66a47408449a6cfa7696fb0";
    public override string Name => "听中国";
    public override string Url => "https://m.i275.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n资源挺全，但未必都能播放。网站已改版，只保留“最近上架”分类。";
    protected override string CoverDomain => "i275.com";

    public string LoginUrl => "https://m.i275.com/";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"https://m.i275.com/search.php?q={Enc(keywords)}&page={page}", false, ct);
        var books = new List<Book>();
        foreach (var a in doc.Select(".divide-y > a[href*='/book/']"))
        {
            var ps = a.Select("p");
            string Field(string label) => ps.FirstOrDefault(p => p.Text().StartsWith(label))?.OwnText() ?? "";
            books.Add(new Book(a.SelectFirst("img").AbsUrl("src"), a.AbsUrl("href"), a.SelectFirst("h3").Text(), Field("作者"), Field("演播"))
            {
                Intro = a.SelectFirst("p.line-clamp-2, p.text-gray-400").Text(),
            });
        }
        // 网站一次返回全部结果
        return new SearchResult(books, page);
    }

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        IReadOnlyList<CategoryMenu> menus = new[] { new CategoryMenu("推荐", new[] { new CategoryTab("最近上架", "https://m.i275.com/") }) };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, false, ct);
        var books = new List<Book>();
        foreach (var a in doc.Select(".grid > a[href*='/book/']"))
        {
            var lines = a.Select(".p-2 > div");
            books.Add(new Book(a.SelectFirst("img").AbsUrl("src"), a.AbsUrl("href"), lines.ElementAtOrDefault(0).Text(), "",
                lines.ElementAtOrDefault(1).Text().Replace("演播", "").Trim()));
        }
        return new CategoryPage(books, 1, 1, url, null);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var doc = await GetDoc(bookUrl, false, ct);
        var list = loadEpisodes
            ? doc.Select("a[id^='chapter-pos-']")
                .Select(a => new Episode(a.Select("span").ElementAtOrDefault(1).Text() is { Length: > 0 } t ? t : a.Text(), a.AbsUrl("href")))
                .ToList()
            : new List<Episode>();

        string Info(string label) =>
            doc.Select("p").FirstOrDefault(p => p.Text().StartsWith(label + "："))?.SelectFirst("span").Text() ?? "";
        return new BookDetail(list, doc.SelectFirst("p.leading-relaxed").Text())
        {
            Author = Info("作者"),
            Artist = Info("演播"),
            Status = Info("状态"),
            CoverUrl = doc.SelectFirst(".shadow-lg img").AbsUrl("src"),
        };
    }

    /// <summary>
    /// 播放页需要先访问书籍页拿到会话 Cookie，否则会跳回首页；音频地址在 APlayer 配置的 url 字段（带签名，需每次现取）
    /// </summary>
    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom(async (url, ct) =>
    {
        var m = System.Text.RegularExpressions.Regex.Match(url, @"/play/(\d+)/");
        var bookUrl = m.Success ? $"https://m.i275.com/book/{m.Groups[1].Value}.html" : Url;
        await Host.GetStringAsync(bookUrl, ct: ct);
        var html = await Host.GetStringAsync(url, new RequestOptions { Headers = new() { ["Referer"] = bookUrl } }, ct);
        var a = System.Text.RegularExpressions.Regex.Match(html, @"audio\s*:\s*\[\s*\{.*?url\s*:\s*""([^""]+)""", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!a.Success) return await Host.SniffMediaAsync(url, ct: ct);
        var audio = System.Text.RegularExpressions.Regex.Unescape(a.Groups[1].Value);
        if (audio.StartsWith("http")) return audio;
        if (audio.StartsWith("lrts$")) return await FromLrts(audio, ct);
        throw new InvalidOperationException("网站没有提供这一集的播放地址");
    });

    /// <summary>
    /// 部分书籍来自懒人听书，网站只给出占位符 lrts$书ID#章节ID#序号#...（网页版本身也无法播放），
    /// 这里改用懒人听书的公开接口获取，只有免费章节可以播放
    /// </summary>
    private async Task<string> FromLrts(string placeholder, CancellationToken ct)
    {
        var parts = placeholder[5..].Split('#');
        if (parts.Length < 3) throw new InvalidOperationException("无法识别的播放地址：" + placeholder);
        var json = await Host.GetJsonAsync(
            $"https://m.lrts.me/ajax/getPlayPath?entityId={parts[0]}&entityType=3&opType=1&sections=[{parts[2]}]&type=0", ct: ct);
        var path = json.Str("list.0.path");
        if (path.StartsWith("http")) return path;
        var msg = json.Str("msg");
        throw new InvalidOperationException("这本书的音频来自懒人听书" + (msg.Length > 0 ? $"：{msg}" : "，这一集无法播放"));
    }
}
