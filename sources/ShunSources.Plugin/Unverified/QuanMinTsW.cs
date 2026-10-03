using TingShu.Sdk;

namespace ShunSources;

/// <summary>全民听书网 qmtsw.com</summary>
public sealed class QuanMinTsW : PageBarCmsSource
{
    public override string Id => "b21eafb29cbb42fea6a939528c699a23";
    public override string Name => "全民听书网";
    protected override string Site => "https://www.qmtsw.com/";
    public override string Description => "推荐指数:3星 ⭐⭐⭐\n【已失效】网站目前返回 520 错误，源保留以便网站恢复。";
    public override bool EnabledByDefault => false;
}
