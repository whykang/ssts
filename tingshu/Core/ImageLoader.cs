using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TingShu.Sdk;

namespace tingshu.Core;

/// <summary>
/// 封面异步加载：内存 + 磁盘缓存，支持插件提供的请求头（防盗链），自动缩放解码以节省内存。
/// 用法：&lt;Image core:ImageLoader.Url="{Binding CoverUrl}" core:ImageLoader.SourceId="{Binding SourceId}" /&gt;
/// </summary>
public static class ImageLoader
{
    private const int DecodeWidth = 360;
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Pending = new();
    private static readonly LinkedList<string> LruKeys = new();
    private static readonly Dictionary<string, ImageSource> Memory = new();
    private static readonly SemaphoreSlim Throttle = new(6);
    private static readonly string DiskDir = AppPaths.Ensure(Path.Combine(AppPaths.CacheDir, "covers"));

    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(string), typeof(ImageLoader), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty SourceIdProperty = DependencyProperty.RegisterAttached(
        "SourceId", typeof(string), typeof(ImageLoader), new PropertyMetadata(null));

    public static string? GetUrl(DependencyObject d) => (string?)d.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject d, string? v) => d.SetValue(UrlProperty, v);
    public static string? GetSourceId(DependencyObject d) => (string?)d.GetValue(SourceIdProperty);
    public static void SetSourceId(DependencyObject d, string? v) => d.SetValue(SourceIdProperty, v);

    private static async void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var url = e.NewValue as string;
        SetTarget(d, null);
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

        if (Memory.TryGetValue(url, out var cached))
        {
            SetTarget(d, cached);
            return;
        }
        // 等绑定的 SourceId 先生效
        await d.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        var sourceId = GetSourceId(d);
        var image = await Pending.GetOrAdd(url, u => LoadAsync(u, sourceId));
        Pending.TryRemove(url, out _);
        if (image == null) return;
        Remember(url, image);
        if (GetUrl(d) == url) SetTarget(d, image);
    }

    private static void SetTarget(DependencyObject d, ImageSource? source)
    {
        switch (d)
        {
            case Image img: img.Source = source; break;
            case ImageBrush brush: brush.ImageSource = source; break;
        }
    }

    private static void Remember(string url, ImageSource image)
    {
        if (Memory.ContainsKey(url)) return;
        Memory[url] = image;
        LruKeys.AddLast(url);
        while (LruKeys.Count > 400)
        {
            Memory.Remove(LruKeys.First!.Value);
            LruKeys.RemoveFirst();
        }
    }

    private static async Task<ImageSource?> LoadAsync(string url, string? sourceId)
    {
        await Throttle.WaitAsync();
        try
        {
            return await Task.Run(async () =>
            {
                var file = Path.Combine(DiskDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))));
                byte[] bytes;
                if (File.Exists(file))
                {
                    bytes = await File.ReadAllBytesAsync(file);
                }
                else
                {
                    // 网络偶尔卡住时重试一次
                    var downloaded = await DownloadAsync(url, sourceId);
                    if (downloaded == null) return null;
                    bytes = downloaded;
                    await File.WriteAllBytesAsync(file, bytes);
                }
                return Decode(bytes);
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"封面加载失败 {url}：{(ex is TaskCanceledException ? "超时" : ex.Message)}");
            return null;
        }
        finally
        {
            Throttle.Release();
        }
    }

    private static async Task<byte[]?> DownloadAsync(string url, string? sourceId)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", HttpService.UserAgent(false));
                request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/*,*/*;q=0.8");
                var entry = PluginManager.Instance.Find(sourceId);
                var headers = new Dictionary<string, string>();
                if (entry?.Source is ICoverHeaders ch && ch.TryGetCoverHeaders(url, headers))
                {
                    foreach (var (k, v) in headers)
                    {
                        request.Headers.Remove(k);
                        request.Headers.TryAddWithoutValidation(k, v);
                    }
                }
                else if (entry != null && Uri.TryCreate(entry.Source.Url, UriKind.Absolute, out var site))
                {
                    request.Headers.Referrer = new Uri(site.GetLeftPart(UriPartial.Authority) + "/");
                }
                using var response = await HttpService.Client.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsByteArrayAsync(timeout.Token);
            }
            catch (Exception ex) when (attempt == 0 && ex is TaskCanceledException or HttpRequestException)
            {
                await Task.Delay(500);
            }
        }
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = DecodeWidth;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null; // 不支持的格式（例如系统缺少 WebP 解码器）
        }
    }

    public static void ClearDiskCache()
    {
        foreach (var f in Directory.GetFiles(DiskDir))
        {
            try { File.Delete(f); } catch { }
        }
    }

    public static long DiskCacheSize() =>
        Directory.Exists(AppPaths.CacheDir)
            ? new DirectoryInfo(AppPaths.CacheDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;
}
