namespace TingShu.Sdk;

/// <summary>
/// 所有书源的基类。
/// 子类需要有无参构造函数；宿主创建实例后会调用 <see cref="Initialize"/> 注入 <see cref="Host"/>。
/// 所有异步方法都在后台线程调用。
/// </summary>
public abstract class SourceBase
{
    private ISourceHost? _host;

    /// <summary>宿主能力（网络请求、WebView、配置、提示……）</summary>
    protected ISourceHost Host => _host ?? throw new InvalidOperationException("源尚未初始化");

    /// <summary>由宿主调用</summary>
    public virtual void Initialize(ISourceHost host) => _host = host;

    /// <summary>唯一 ID，建议使用 GUID（不带横线），发布后不要修改，否则书架中的书会失效</summary>
    public abstract string Id { get; }

    /// <summary>站点名称</summary>
    public abstract string Name { get; }

    /// <summary>站点网址</summary>
    public abstract string Url { get; }

    /// <summary>站点简介</summary>
    public virtual string Description => "";

    /// <summary>分组，例如 “听书”、“广播”</summary>
    public virtual string Group => "听书";

    /// <summary>是否支持搜索</summary>
    public virtual bool IsSearchable => true;

    /// <summary>是否支持发现（分类）</summary>
    public virtual bool IsDiscoverable => true;

    /// <summary>章节列表是否需要分页加载（加载耗时较长）</summary>
    public virtual bool IsMultipleEpisodePages => false;

    /// <summary>首次安装时是否默认启用（用户手动切换后以用户设置为准）</summary>
    public virtual bool EnabledByDefault => true;

    /// <summary>搜索</summary>
    public virtual Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
        => Task.FromResult(SearchResult.Empty);

    /// <summary>分类菜单</summary>
    public virtual Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CategoryMenu>>(Array.Empty<CategoryMenu>());

    /// <summary>分类列表的一页。翻页时会传入上一页返回的 NextUrl</summary>
    public virtual Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
        => Task.FromResult(new CategoryPage());

    /// <summary>
    /// 书籍详情和章节列表。
    /// loadEpisodes 为 false 时只需要补充书籍信息；
    /// loadFullPages 为 false 时，分页章节只加载第一页即可。
    /// </summary>
    public abstract Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct);

    /// <summary>音频提取策略</summary>
    public abstract AudioExtractor GetAudioExtractor();
}

/// <summary>
/// 插件入口（可选）。一个程序集中如果有实现了此接口的类，宿主只会加载它返回的源；
/// 否则自动加载程序集中所有继承 <see cref="SourceBase"/> 的公开类。
/// </summary>
public interface ISourceProvider
{
    string Name { get; }
    string Description => "";
    IEnumerable<SourceBase> GetSources();
}

/// <summary>播放音频时需要额外请求头（例如 Referer 防盗链）</summary>
public interface IAudioHeaders
{
    IDictionary<string, string>? GetAudioHeaders(string audioUrl);
}

/// <summary>
/// 加载封面时需要额外请求头。务必判断 coverUrl 属于自己再修改 headers 并返回 true。
/// </summary>
public interface ICoverHeaders
{
    bool TryGetCoverHeaders(string coverUrl, IDictionary<string, string> headers);
}

/// <summary>需要登录的源。登录在 WebView 中完成，Cookie 可通过 Host.GetWebViewCookiesAsync 取得</summary>
public interface ILoginSource
{
    string LoginUrl { get; }
    bool LoginDesktop => false;
}

/// <summary>
/// 搜索需要人机验证的源。搜索失败或没有结果时，搜索页会显示“验证”按钮，
/// 用户在弹出的网页中完成验证后自动重新搜索。请求时用 RequestOptions.UseWebViewCookies 带上验证后的 Cookie。
/// </summary>
public interface ISearchVerification
{
    string GetSearchVerificationUrl(string keywords);
    bool SearchVerificationDesktop => true;

    /// <summary>验证完成后等待多少秒再搜索（部分网站限制两次搜索的最小间隔）</summary>
    int SearchDelaySeconds => 0;

    /// <summary>
    /// 根据验证页面的标题判断是否已通过验证（通过后验证窗口自动关闭）。默认：标题不再包含“验证”。
    /// </summary>
    bool IsVerificationPassed(string pageTitle) => !string.IsNullOrEmpty(pageTitle) && !pageTitle.Contains("验证");
}

/// <summary>
/// 部分网站的 HTTPS 证书有效但域名不匹配（例如 CDN 配置错误）。
/// 实现此接口声明这些主机名后，宿主只对这些主机、且只在“仅域名不匹配”时放行；其它证书错误仍会拒绝。
/// </summary>
public interface ICertificateNameMismatchHosts
{
    IReadOnlyCollection<string> NameMismatchHosts { get; }
}

/// <summary>可配置的源，在插件管理页面中点击“设置”打开</summary>
public interface IConfigurableSource
{
    IReadOnlyList<ConfigItem> GetConfigItems();
}

/// <summary>播放状态回调：playing / paused / stopped / error</summary>
public interface IPlaybackStateListener
{
    void OnPlaybackStateChanged(string state);
}

/// <summary>
/// 增量更新章节：章节很多、整本加载很慢（或网站限制访问频率）的源可以实现。
/// 本地已有这本书的章节缓存时，宿主改为调用 <see cref="UpdateEpisodesAsync"/>，不再整本重新加载。
/// </summary>
public interface IIncrementalEpisodes
{
    /// <summary>
    /// 根据已有章节（按网站原始顺序）返回更新后的完整详情；返回 null 表示无法增量更新，宿主会整本重新加载。
    /// 上次没加载完整时（例如被限流），可以从断开的位置接着加载。
    /// </summary>
    Task<BookDetail?> UpdateEpisodesAsync(string bookUrl, IReadOnlyList<Episode> known, CancellationToken ct);
}
