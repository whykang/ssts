using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TingShu.Sdk;

namespace ShunSources;

/// <summary>众听（api.yituwenhua.com 聚合接口）的公共实现</summary>
public abstract class ZhongTingBase : ShunSource
{
    private const string Api = "https://api.yituwenhua.com/api/json/v1/";
    private const int PageSize = 15;

    public override string Url => "https://www.baidu.com/";
    public override string Description => "推荐指数:4星 ⭐⭐⭐⭐\n【已失效】接口域名 api.yituwenhua.com 目前已改作其它网站，源保留以便恢复。";
    public override bool EnabledByDefault => false;
    protected override string CoverDomain => "imgopen.xmcdn.com/";
    protected override string Referer => "https://www.ximalaya.com";

    private static string SearchUrl(string keyword, string from, int page) =>
        $"{Api}MultiSearch?keyword={Uri.EscapeDataString(keyword)}&fromLy={from}&apiLy=xmly&pageNum={page}&pageSize={PageSize}";

    public override async Task<SearchResult> SearchAsync(string keywords, int page, CancellationToken ct)
    {
        var books = ParseBooks(await Host.GetJsonAsync(SearchUrl(keywords, "title", page), ct: ct));
        return new SearchResult(books, books.Count >= PageSize ? page + 1 : page);
    }

    public override Task<IReadOnlyList<CategoryMenu>> GetCategoryMenusAsync(CancellationToken ct)
    {
        static CategoryMenu M(string title, string tags) =>
            new(title, tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Select(t => new CategoryTab(t, SearchUrl(t, "tag", 1))).ToList());

        IReadOnlyList<CategoryMenu> menus = new[]
        {
            M("有声书", "言情 健身 同人 幻想 网游 都市重生 文学名著 历史专区 国家经典 电竞 虚拟现实 历史纵横 有声漫 看小说 大家都在追 豪门恋情 古风言情 宫闱宅斗 传记 修真 仙侠 异世大陆 重生 扮猪吃虎 创业 赛博朋克 怪谈 甜宠 总裁 霸道 古风 名人演讲 销售 学习方法 思维训练 专注力 悬疑 武侠 都市 历史 言情女生"),
            M("相声评书", "单田芳 青曲社 单口相声 名家评书 新锐笑将 岳云鹏 刘兰芳 王玥波 郭鹤鸣 武启深 武宗亮 郑思杰 张准 雍正剑侠图"),
            M("悬疑推理", "侦察推理 侦探推理 罪案调查 逻辑推理 反转推理 刑侦推理 破案实录 民间怪谈 诡异故事 都市传说 侦探小说 刑侦破案 法医 记者调查 剧本 平行时空 黑客 科技 盗墓笔记"),
            M("历史", "中国史 世界史 历史人物 逸闻趣事 文物考古 兵法史书 历史剧场 历史小说 文明史 军事历史 现当代 民国 宋辽金元 隋唐 魏晋 南北朝 五代十国 先秦 明清 三国 秦汉 三十六计 孙子兵法 史记"),
            M("个人成长", "职业技能 人际沟通 心理调节 名人演讲 高效管理 女性成长 家庭教育 畅销书 名企大咖 成功励志 时间管理 自律习惯 情商提升 领导力 职场 心理学 演讲口才 谈判技巧 社交礼仪 情绪管理 情感修复 健康习惯 极简生活 科技创新"),
            M("广播剧", "现代言情 古风言情 有声漫画 心动剧场 国风剧场 尖声剧场"),
            M("娱乐", "奇闻怪谈 八卦 脱口秀 综艺 刑侦探案"),
            M("头条", "民生 财经 科技 娱乐 传记 足球 篮球"),
            M("商业财经", "大咖评论 投资理财 证券市场 创投创业 商业经管"),
            M("音乐", "催眠 新歌 经典老歌 翻唱 古典 古风 纯音乐 怀旧 影视 欧美 民谣 日韩 创作达人"),
            M("健康养生", "黄帝内经 吃出健康 我要减肥 十月妈咪 两性奥秘 运动健身 中医养生 心灵解压 古法艾灸 轻松入睡 健康宝宝 美丽驻颜"),
        };
        return Task.FromResult(menus);
    }

    public override async Task<CategoryPage> GetCategoryPageAsync(string url, CancellationToken ct)
    {
        var books = ParseBooks(await Host.GetJsonAsync(url, ct: ct));
        var page = RegexInt(url, @"pageNum=(\d+)");
        // 原版只取第一页；接口支持 pageNum，这里加上翻页
        string? next = books.Count >= PageSize ? Regex.Replace(url, @"pageNum=\d+", $"pageNum={page + 1}") : null;
        return new CategoryPage(books, page, next != null ? page + 1 : page, url, next);
    }

    private static List<Book> ParseBooks(JsonNode json) => json.Items("data").Take(50).Select(b =>
        new Book(b.Str("coverImageUrl"), $"{Api}albums_browse_search?album_id={b.Str("novelId")}", b.Str("novelName"), "", b.Str("presenter"))
        {
            Intro = b.Str("description"),
            Status = b.Str("novelStatus"),
        }).ToList();

    public override async Task<BookDetail> GetBookDetailAsync(string bookUrl, bool loadEpisodes, bool loadFullPages, CancellationToken ct)
    {
        var list = new List<Episode>();
        if (loadEpisodes)
        {
            var json = await Host.GetJsonAsync(bookUrl, ct: ct);
            foreach (var ch in json.Items("data"))
            {
                var play = ch.Str("playUrl");
                if (play.Length == 0 || play == "null")
                {
                    // 没有公开播放地址（通常是付费 / 会员章节）
                    list.Add(new Episode(ch.Str("chapterName"), "track:" + ch.Str("chapterId")) { IsFree = false });
                }
                else list.Add(new Episode(ch.Str("chapterName"), play));
            }
        }
        return new BookDetail(list);
    }

    public override AudioExtractor GetAudioExtractor() => AudioExtractor.Custom((url, _) =>
        url.StartsWith("http")
            ? Task.FromResult(url)
            : throw new InvalidOperationException("这一集没有公开的播放地址（可能是付费或会员内容），无法播放"));
}

/// <summary>众听</summary>
public sealed class RenrenTing : ZhongTingBase
{
    public override string Id => "366b8e910c2a43729d02926eee60abd7";
    public override string Name => "众听";
}

/// <summary>众听（备用）—— 原 RenrenTing2，与 RenrenTing 原本共用 ID，这里换成新 ID</summary>
public sealed class RenrenTing2 : ZhongTingBase
{
    public override string Id => "5f1e2d3c4b5a49688776a5b4c3d2e1f0";
    public override string Name => "众听（备用）";
}
