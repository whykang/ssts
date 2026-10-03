using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using TingShu.Sdk;

namespace tingshu.Core;

/// <summary>
/// 全局共享的 HTTP 客户端：自动解压、Cookie、系统代理、编码识别（GBK/GB2312/Big5…）
/// </summary>
public static partial class HttpService
{
    static HttpService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static CookieContainer Cookies { get; } = new();

    public static HttpClient Client { get; } = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        CookieContainer = Cookies,
        UseCookies = true,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        SslOptions = { RemoteCertificateValidationCallback = ValidateCertificate },
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>插件声明的“证书域名不匹配”例外主机（见 ICertificateNameMismatchHosts）</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> NameMismatchHosts = new(StringComparer.OrdinalIgnoreCase);

    public static void AllowNameMismatch(IEnumerable<string> hosts)
    {
        foreach (var h in hosts) NameMismatchHosts[h.Trim()] = 0;
    }

    /// <summary>
    /// 正常情况完全按系统规则校验；只有证书链有效、仅域名不匹配，且主机在插件声明的例外列表中时才放行
    /// </summary>
    private static bool ValidateCertificate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
        System.Security.Cryptography.X509Certificates.X509Chain? chain, System.Net.Security.SslPolicyErrors errors)
    {
        if (errors == System.Net.Security.SslPolicyErrors.None) return true;
        if (errors != System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) return false;
        var host = (sender as System.Net.Security.SslStream)?.TargetHostName;
        return host != null && NameMismatchHosts.ContainsKey(host);
    }

    public static string UserAgent(bool desktop) =>
        desktop ? AppSettings.Current.DesktopUA : AppSettings.Current.MobileUA;

    public static async Task<HttpResponseMessage> SendAsync(string url, RequestOptions? options, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        options ??= new RequestOptions();
        var request = new HttpRequestMessage(new HttpMethod(options.Method.ToUpperInvariant()), url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent(options.Desktop));
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/json,application/xhtml+xml,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

        if (options.Body != null)
        {
            var encoding = (options.Encoding != null ? GetEncoding(options.Encoding) : null) ?? Encoding.UTF8;
            request.Content = new StringContent(options.Body, encoding);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                options.ContentType ?? "application/x-www-form-urlencoded") { CharSet = encoding.WebName };
        }

        if (options.Headers != null)
        {
            foreach (var (k, v) in options.Headers)
            {
                if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && request.Content != null)
                {
                    request.Content.Headers.Remove(k);
                    request.Content.Headers.TryAddWithoutValidation(k, v);
                    continue;
                }
                request.Headers.Remove(k);
                request.Headers.TryAddWithoutValidation(k, v);
            }
        }

        if (options.UseWebViewCookies)
        {
            // 把 WebView 的 Cookie（登录 / 验证后的会话）同步进共享的 CookieContainer，同名覆盖。
            // 不能另外手写 Cookie 头：容器里可能已有网站之前发给 HttpClient 的同名会话（如未验证的 PHPSESSID），
            // 两份一起发出时网站会认旧的那份。
            var webCookies = await WebViewService.Instance.GetCookieListAsync(url);
            if (webCookies.Count > 0 && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var names = webCookies.Select(c => c.Name).ToHashSet();
                foreach (Cookie old in Cookies.GetCookies(uri))
                    if (names.Contains(old.Name)) old.Expired = true;   // 域名写法不同（www. 与 .）时也能替换掉旧的
                foreach (var c in webCookies)
                {
                    try { Cookies.Add(c); }
                    catch (CookieException) { }
                }
            }
        }

        var response = await Client.SendAsync(request, completion, ct);
        return response;
    }

    public static async Task<string> GetStringAsync(string url, RequestOptions? options, CancellationToken ct)
    {
        using var response = await SendAsync(url, options, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}：{url}");
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var charset = options?.Encoding ?? response.Content.Headers.ContentType?.CharSet;
        return Decode(bytes, charset);
    }

    public static string Decode(byte[] bytes, string? charset)
    {
        var encoding = charset != null ? GetEncoding(charset) : null;
        if (encoding == null)
        {
            // 从 meta 中嗅探编码
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            var m = MetaCharset().Match(head);
            if (m.Success) encoding = GetEncoding(m.Groups[1].Value);
        }
        encoding ??= Encoding.UTF8;
        var text = encoding.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    public static Encoding? GetEncoding(string name)
    {
        name = name.Trim().Trim('"', '\'').ToLowerInvariant();
        if (name is "gb2312" or "gbk") name = "gb18030";
        try { return Encoding.GetEncoding(name); }
        catch { return null; }
    }

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?([\w-]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharset();
}
