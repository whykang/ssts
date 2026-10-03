namespace ShunSources;

/// <summary>
/// 囧贼听书网 jiongzei.com —— 与乐听吧、米听书是同一套网站程序（.row3.row-b 列表、.pagebar 分页、播放页 var now）
/// </summary>
public sealed class Jiongzei : PageBarCmsSource
{
    public override string Id => "c1bf15e52e0841a1bc621a0a18bdeaea";
    public override string Name => "囧贼听书网";
    protected override string Site => "https://jiongzei.com/";
    public override string Description => "言情、武侠、悬疑、评书、相声小品、百家讲坛等。搜索如需验证，点击搜索页该源旁的“验证”即可。";
}
