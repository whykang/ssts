using System.Text.RegularExpressions;
using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>
/// 评书随身听 psmp3.com（Z-BlogPHP）。
/// 结构：评书艺人（分类）→ 书（子分类页，如 /stf-styy.html）→ 若干“部分”文章（如 /stf-styy/styy-1.html），
/// 每个部分页的 APlayer 脚本里有约 50 回的音频列表。音频需要带 Referer。
/// </summary>
public sealed partial class Psmp3 : ShunSource, IAudioHeaders
{
    private const string Site = "https://www.psmp3.com/";

    private static readonly (string Name, string Code)[] Artists =
    {
        ("单田芳", "stf"), ("袁阔成", "ykc"), ("田连元", "tly"), ("刘兰芳", "llf"), ("连丽如", "llr"), ("张少佐", "zsz"), ("田战义", "tzy"),
    };

    public override string Id => "a20ead64890d4b1b8fd2a6e5d7b21d43";
    public override string Name => "评书随身听";
    public override string Url => Site;
    public override string Description => "单田芳、袁阔成、田连元、刘兰芳、连丽如、张少佐、田战义的评书全集。";
    public override string Group => "评书";
    public override bool IsMultipleEpisodePages => true;

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"{Site}search.php?q={Enc(keywords)}&page={page}", true, ct);
        // 搜索结果是“部分”文章，按所属的书合并
        var books = new List<Book>();
        var seen = new HashSet<string>();
        foreach (var a in doc.Select(".post_list_li h2 a"))
        {
            var bookUrl = BookUrlOfPart(a.AbsUrl("href"));
            if (bookUrl == null || !seen.Add(bookUrl)) continue;
            books.Add(new Book("", bookUrl, BookTitleOfPart(a.Text())) { Artist = ArtistOf(bookUrl) });
        }
        var hasNext = doc.Select("a").Any(x => x.Text().Contains("下一页"));
        return new SearchResult(books, hasNext ? page + 1 : page);
    }

    /// <summary>/stf-styy/styy-1.html → /stf-styy.html</summary>
    private static string? BookUrlOfPart(string partUrl)
    {
        var m = PartUrl().Match(partUrl);
        return m.Success ? $"{Site}{m.Groups[1].Value}.html" : null;
    }

    /// <summary>“单田芳评书《隋唐演义》全216回(一)在线收听,免费下载” → “隋唐演义 全216回”</summary>
    private static string BookTitleOfPart(string title)
    {
        var m = Regex.Match(title, @"《(.+?)》(全\d+回)?");
        return m.Success ? (m.Groups[1].Value + (m.Groups[2].Success ? " " + m.Groups[2].Value : "")) : title;
    }

    private static string ArtistOf(string bookUrl)
    {
        var code = Regex.Match(bookUrl, @"psmp3\.com/([a-z]+)-").Groups[1].Value;
        return Artists.FirstOrDefault(a => a.Code == code).Name ?? "";
    }

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        IReadOnlyList<CategoryMenu> menus = new[]
        {
            new CategoryMenu("评书艺人", Artists.Select(a => new CategoryTab(a.Name, $"{Site}{a.Code}.html")).ToList()),
        };
        return Task.FromResult(menus);
    }

    /// <summary>艺人页顶部列出了他的全部书（子分类链接）</summary>
    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct);
        var code = Regex.Match(url, @"psmp3\.com/([a-z]+)\.html").Groups[1].Value;
        var artist = Artists.FirstOrDefault(a => a.Code == code).Name ?? "";
        var books = new List<Book>();
        var seen = new HashSet<string>();
        foreach (var a in doc.Select(".article a"))
        {
            var href = a.AbsUrl("href");
            if (!Regex.IsMatch(href, $@"psmp3\.com/{code}-[a-z0-9]+\.html$") || !seen.Add(href)) continue;
            var title = a.Text();
            if (title.StartsWith(artist)) title = title[artist.Length..];
            books.Add(new Book("", href, title, "", artist));
        }
        return new CategoryPage(books, 1, 1, url, null);
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        var doc = await GetDoc(bookUrl, true, ct);
        var intro = doc.SelectFirst(".post_list_li p").Text();
        if (!loadEpisodes) return new BookDetail(list, intro);

        // 书页列出各“部分”，可能分页
        var parts = new List<string>();
        var pages = new HashSet<string> { bookUrl };
        var current = doc;
        for (var i = 0; i < 20 && current != null; i++)
        {
            foreach (var a in current.Select(".post_list_li h2 a"))
            {
                var href = a.AbsUrl("href");
                if (!parts.Contains(href)) parts.Add(href);
            }
            var next = current.Select("a").FirstOrDefault(x => x.Text().Contains("下一页"))?.AbsUrl("href");
            current = next != null && pages.Add(next) ? await GetDoc(next, true, ct) : null;
        }
        // 文章按发布时间倒序时，按“部分”编号排序
        parts = parts.OrderBy(p => RegexInt(p, @"-(\d+)\.html$", 0)).ToList();

        for (var i = 0; i < parts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                if (!loadFullPages) break;
                Host.ReportProgress($"{i + 1} / {parts.Count}");
                await Task.Delay(Random.Shared.Next(200, 500), ct);
            }
            var html = await Host.GetStringAsync(parts[i], RequestOptions.Pc, ct);
            foreach (Match m in AudioItem().Matches(html))
            {
                var url = Regex.Unescape(m.Groups[3].Value);
                if (url.StartsWith("//")) url = "https:" + url;
                list.Add(new Episode(Regex.Unescape(m.Groups[1].Value), url));
            }
        }
        Host.ReportProgress(null);
        return new BookDetail(list, intro) { Artist = ArtistOf(bookUrl) };
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Direct;

    // 不带 Referer 时音频服务器返回 403
    public IDictionary<string, string>? GetAudioHeaders(string audioUrl) =>
        audioUrl.Contains("psmp3.com") ? new Dictionary<string, string> { ["Referer"] = Site } : null;

    // 部分页文件名不统一：/stf-styy/styy-1.html、/llr-dst/llr-dst-390-1.html，只取目录名
    [GeneratedRegex(@"psmp3\.com/([a-z]+-[a-z0-9]+)/[^/]+\.html")]
    private static partial Regex PartUrl();

    [GeneratedRegex(@"name:\s*""((?:[^""\\]|\\.)*)""\s*,\s*artist:\s*""((?:[^""\\]|\\.)*)""\s*,\s*url:\s*""([^""]+)""")]
    private static partial Regex AudioItem();
}
