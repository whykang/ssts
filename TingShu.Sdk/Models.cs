namespace TingShu.Sdk;

/// <summary>
/// 书籍（专辑）。列表、搜索结果、书架中使用的基础信息。
/// </summary>
public class Book
{
    public Book() { }

    public Book(string coverUrl, string bookUrl, string title, string author = "", string artist = "")
    {
        CoverUrl = coverUrl;
        BookUrl = bookUrl;
        Title = title;
        Author = author;
        Artist = artist;
    }

    /// <summary>封面地址</summary>
    public string CoverUrl { get; set; } = "";
    /// <summary>书籍地址，同一个源内必须唯一，它和 SourceId 一起确定一本书</summary>
    public string BookUrl { get; set; } = "";
    /// <summary>标题</summary>
    public string Title { get; set; } = "";
    /// <summary>作者</summary>
    public string Author { get; set; } = "";
    /// <summary>演播</summary>
    public string Artist { get; set; } = "";
    /// <summary>简介</summary>
    public string Intro { get; set; } = "";
    /// <summary>状态，例如 “完本 | 共120集”</summary>
    public string Status { get; set; } = "";
    /// <summary>所属源 ID。宿主会自动填充，插件可以不设置</summary>
    public string SourceId { get; set; } = "";
    /// <summary>是否已完结</summary>
    public bool IsCompleted { get; set; }
}

/// <summary>
/// 章节
/// </summary>
public class Episode
{
    public Episode() { }

    public Episode(string title, string url)
    {
        Title = title;
        Url = url;
    }

    public string Title { get; set; } = "";
    /// <summary>章节地址，交给 <see cref="AudioExtractor"/> 解析成最终音频地址</summary>
    public string Url { get; set; } = "";
    /// <summary>是否免费</summary>
    public bool IsFree { get; set; } = true;
    /// <summary>章节封面（可选）</summary>
    public string CoverUrl { get; set; } = "";
}

/// <summary>
/// 书籍详情。除章节列表外，其它字段不为空时会补充到书籍信息中。
/// </summary>
public class BookDetail
{
    public BookDetail() { }

    public BookDetail(IList<Episode> episodes, string? intro = null)
    {
        Episodes = episodes;
        Intro = intro;
    }

    public IList<Episode> Episodes { get; set; } = new List<Episode>();
    public string? Intro { get; set; }
    public string? Author { get; set; }
    public string? Artist { get; set; }
    public string? CoverUrl { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    /// <summary>章节总数（可选，仅用于展示）</summary>
    public int EpisodesCount { get; set; }
}

/// <summary>分类标签（子分类）</summary>
public record CategoryTab(string Title, string Url);

/// <summary>大分类，包含若干子分类</summary>
public record CategoryMenu(string Title, IReadOnlyList<CategoryTab> Tabs);

/// <summary>
/// 分类页面的一页书籍
/// </summary>
public class CategoryPage
{
    public CategoryPage() { }

    public CategoryPage(IList<Book> books, int currentPage, int totalPage, string currentUrl, string? nextUrl)
    {
        Books = books;
        CurrentPage = currentPage;
        TotalPage = totalPage;
        CurrentUrl = currentUrl;
        NextUrl = nextUrl;
    }

    public IList<Book> Books { get; set; } = new List<Book>();
    public int CurrentPage { get; set; } = 1;
    public int TotalPage { get; set; } = 1;
    public string CurrentUrl { get; set; } = "";
    /// <summary>下一页地址，为空表示没有下一页</summary>
    public string? NextUrl { get; set; }

    public bool HasMore => !string.IsNullOrEmpty(NextUrl) && CurrentPage < TotalPage;
}

/// <summary>
/// 搜索结果
/// </summary>
/// <param name="Books">本页结果</param>
/// <param name="TotalPage">总页数。无法得知时，确定有下一页可返回 当前页 + 1</param>
public record SearchResult(IList<Book> Books, int TotalPage)
{
    public static SearchResult Empty { get; } = new(new List<Book>(), 1);
}

/// <summary>
/// 源设置项。宿主保存的 key 为 “源ID.key”，在插件中用 Host.GetPref(key) 读取（无需加前缀）。
/// </summary>
public abstract record ConfigItem(string Key, string Label)
{
    public sealed record Text(string Key, string Label, string Default = "") : ConfigItem(Key, Label);
    public sealed record Switch(string Key, string Label, bool Default = false) : ConfigItem(Key, Label);
    public sealed record Select(string Key, string Label, IReadOnlyList<string> Options, string Default) : ConfigItem(Key, Label);
    public sealed record Button(string Label, Action Click) : ConfigItem("", Label);
}
