using AngleSharp.Dom;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>悦听吧 yuetingba.cn</summary>
public sealed class YuetingBa : ShunSource
{
    public override string Id => "cc17e42c42fc434aa13f783567a947db";
    public override string Name => "悦听吧";
    public override string Url => "http://www.yuetingba.cn/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n不是所有都能听，有的可能要会员。";
    protected override string CoverDomain => "www.yuetingba.cn/";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var doc = await GetDoc($"http://www.yuetingba.cn/search?type=1&name={Enc(keywords)}&pageIndex={page}", true, ct, withCookies: true);
        var (current, total, _) = ParsePager(doc);
        return new SearchResult(ParseBooks(doc), total > 0 ? total : current);
    }

    public override async Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        var doc = await GetDoc(Url, true, ct);
        var tabs = doc.Select(".nav.navbar-nav li > a").Where(a => a.Text() != "首页").Select(a => new CategoryTab(a.Text(), a.AbsUrl("href"))).ToList();
        return new[] { new CategoryMenu("分类", tabs) };
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var doc = await GetDoc(url, true, ct, withCookies: true);
        var (current, total, next) = ParsePager(doc);
        return new CategoryPage(ParseBooks(doc), current, total, url, next);
    }

    private static (int Current, int Total, string? Next) ParsePager(IDocument doc)
    {
        var pager = doc.GetElementById("PageContent");
        var current = RegexInt(pager?.SelectFirst(".current").Text(), @"(\d+)");
        var next = pager == null ? null : FindByText(pager, "a", "下一页");
        return (current, next != null ? current + 1 : current, next?.AbsUrl("href"));
    }

    private static List<Book> ParseBooks(IDocument doc)
    {
        var books = new List<Book>();
        foreach (var li in doc.SelectFirst(".section-box")?.Select(".section-box-list-item") ?? Enumerable.Empty<IElement>())
        {
            var a = li.SelectFirst(".box-list-item-text > .box-list-item-text-title > a");
            if (a == null) continue;
            var spans = li.SelectFirst(".box-list-item-text > .box-list-item-text-autspeaker")?.Select("span");
            books.Add(new Book(li.SelectFirst(".box-list-item-img > a > img").AbsUrl("src"), a.AbsUrl("href"), a.Text(),
                spans?.ElementAtOrDefault(0)?.SelectFirst("a").Text() ?? "",
                string.Join(" ", spans?.ElementAtOrDefault(1)?.Select("a").Select(x => x.Text()) ?? Array.Empty<string>()))
            {
                Intro = li.SelectFirst(".box-list-item-text > .box-list-item-text-intro.text-desc-content").Text(),
            });
        }
        return books;
    }

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (loadEpisodes)
        {
            var doc = await GetDoc(bookUrl, true, ct, withCookies: true);
            foreach (var item in doc.Select(".ting-list-content.row .col-md-3.col-xs-12"))
            {
                var a = item.SelectFirst(".col-md-10.col-xs-10 > a");
                if (a == null) continue;
                // 章节通过页面上的 testFn('id') 在播放器 iframe 中播放；记录书籍页地址和章节 id
                var id = a.Attr("onclick").Replace("testFn('", "").Replace("')", "");
                list.Add(new Episode(a.Text(), $"{bookUrl.Split('#')[0]}#ting={id}"));
            }
        }
        return new BookDetail(list);
    }

    /// <summary>
    /// 网站接口返回的是加密地址。这里在书籍页里调用网站自己的 testFn(章节id)，
    /// 由页面在播放器 iframe 中解密并设置 &lt;video&gt; 的地址（带 token / expire 时效签名，不会自动播放），
    /// 然后直接读取这个地址。
    /// </summary>
    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom(async (url, ct) =>
    {
        var parts = url.Split("#ting=");
        if (parts.Length != 2) throw new InvalidOperationException("章节地址格式不正确，请刷新章节列表");
        var id = System.Text.RegularExpressions.Regex.Replace(parts[1], "[^0-9a-fA-F-]", "");
        var script =
            "(function(){var f=document.getElementById('iframe_tingPlay');var w=f&&f.contentWindow;" +
            "if(!w||!w.testFun||!w.document)return '';" +
            "var cur=function(){var v=w.document.querySelector('video,audio');return v?(v.currentSrc||v.src||''):'';};" +
            // 播放器可能预载着上次播放的地址：记下调用前的地址，只接受变化后的新地址（4 秒后仍相同说明本来就是同一集）
            $"if(window.__tsId!=='{id}'){{window.__tsId='{id}';window.__tsPrev=cur();window.__tsAt=Date.now();try{{testFn('{id}');}}catch(e){{}}return '';}}" +
            "var s=cur();if(s.indexOf('http')!==0)return '';" +
            "if(s===window.__tsPrev&&Date.now()-window.__tsAt<4000)return '';" +
            "return s;})();";
        var src = await Host.RenderAsync(parts[0], true, script, TimeSpan.FromSeconds(30), ct);
        return src.Trim('"');
    });
}
