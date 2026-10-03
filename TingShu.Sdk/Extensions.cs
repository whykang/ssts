using System.Text.Json.Nodes;
using AngleSharp.Dom;

namespace TingShu.Sdk;

/// <summary>
/// 解析 HTML 的便捷方法
/// </summary>
public static class DomExtensions
{
    /// <summary>去掉首尾空白并合并连续空白的文本</summary>
    public static string Text(this IElement? element)
        => element == null ? "" : NormalizeSpace(element.TextContent);

    /// <summary>只取元素自身的文本（不含子元素）</summary>
    public static string OwnText(this IElement? element)
    {
        if (element == null) return "";
        var text = string.Concat(element.ChildNodes.Where(n => n.NodeType == NodeType.Text).Select(n => n.TextContent));
        return NormalizeSpace(text);
    }

    /// <summary>读取属性，不存在返回空字符串</summary>
    public static string Attr(this IElement? element, string name)
        => element?.GetAttribute(name)?.Trim() ?? "";

    /// <summary>读取属性并转成绝对地址</summary>
    public static string AbsUrl(this IElement? element, string name)
    {
        var value = element.Attr(name);
        if (value.Length == 0 || element == null) return "";
        return ResolveUrl(element.BaseUri, value);
    }

    /// <summary>查找第一个元素（CSS 选择器）</summary>
    public static IElement? SelectFirst(this IParentNode node, string selector) => node.QuerySelector(selector);

    /// <summary>查找所有元素（CSS 选择器）</summary>
    public static IHtmlCollection<IElement> Select(this IParentNode node, string selector) => node.QuerySelectorAll(selector);

    public static string ResolveUrl(string? baseUrl, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (url.StartsWith("//")) url = "https:" + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https")) return abs.ToString();
        if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) && Uri.TryCreate(b, url, out var r))
            return r.ToString();
        return url;
    }

    public static string NormalizeSpace(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        var space = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || c == ' ')
            {
                space = true;
                continue;
            }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>
/// JSON 便捷方法
/// </summary>
public static class JsonExtensions
{
    /// <summary>按路径读取节点，如 "data.list.0.name"</summary>
    public static JsonNode? At(this JsonNode? node, string path)
    {
        if (node == null) return null;
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node == null) return null;
            if (node is JsonArray arr)
            {
                if (!int.TryParse(raw, out var i)) return null;
                if (i < 0) i += arr.Count;
                node = i >= 0 && i < arr.Count ? arr[i] : null;
            }
            else if (node is JsonObject obj)
            {
                node = obj.TryGetPropertyValue(raw, out var v) ? v : null;
            }
            else return null;
        }
        return node;
    }

    /// <summary>读取字符串（数字、布尔也会转成字符串），不存在返回空字符串</summary>
    public static string Str(this JsonNode? node, string? path = null)
    {
        var n = path == null ? node : node.At(path);
        if (n == null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            return v.ToJsonString();
        }
        return n.ToJsonString();
    }

    public static long Long(this JsonNode? node, string? path = null, long defaultValue = 0)
        => long.TryParse(node.Str(path), out var v) ? v : defaultValue;

    public static int Int(this JsonNode? node, string? path = null, int defaultValue = 0)
        => int.TryParse(node.Str(path), out var v) ? v : defaultValue;

    /// <summary>读取数组，不存在返回空数组</summary>
    public static IEnumerable<JsonNode> Items(this JsonNode? node, string? path = null)
    {
        var n = path == null ? node : node.At(path);
        return n is JsonArray a ? a.Where(x => x != null).Select(x => x!) : Array.Empty<JsonNode>();
    }
}
