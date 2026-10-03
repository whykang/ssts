using System.Net;
using System.Text.RegularExpressions;

namespace tingshu.Core;

public static partial class TextUtil
{
    /// <summary>把插件返回的简介（可能带 HTML）转成纯文本</summary>
    public static string CleanHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (text.Contains('<') && text.Contains('>'))
        {
            text = LineBreakTags().Replace(text, "\n");
            text = Tags().Replace(text, "");
        }
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ').Replace("\r", "");
        text = BlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h\d)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLines();
}
