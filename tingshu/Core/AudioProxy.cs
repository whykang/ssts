using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace tingshu.Core;

/// <summary>
/// 本地音频代理（仅监听 127.0.0.1）。
/// 系统播放器无法自定义请求头，也可能被部分 CDN 拒绝，
/// 所以音频统一经由这里用 HttpClient 转发：附带 UA / Referer / Cookie，支持 Range 拖动进度、系统代理。
/// 对不支持 Range 的服务器，会在后台把整个文件下载到临时文件，再按 Range 从临时文件提供数据，保证可以拖动进度。
/// </summary>
public sealed class AudioProxy
{
    public static AudioProxy Instance { get; } = new();

    private readonly ConcurrentDictionary<string, (string Url, IDictionary<string, string>? Headers)> _map = new();
    private readonly ConcurrentDictionary<string, DownloadCache> _caches = new();
    private readonly string _cacheDir = AppPaths.Ensure(Path.Combine(AppPaths.CacheDir, "audio"));
    private TcpListener? _listener;
    private int _port;

    public void Start()
    {
        if (_listener != null) return;
        CleanupCacheDir();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoop();
        Logger.Info($"音频代理已启动：127.0.0.1:{_port}");
    }

    public Uri Register(string url, IDictionary<string, string>? headers)
    {
        Start();
        var token = Guid.NewGuid().ToString("N");
        _map[token] = (url, headers);
        if (_map.Count > 200)
        {
            foreach (var key in _map.Keys.Take(100)) _map.TryRemove(key, out _);
        }
        // 只保留最近几个整文件缓存，删除其余的临时文件
        foreach (var old in _caches.Keys.Where(k => k != token).ToList().SkipLast(2))
            if (_caches.TryRemove(old, out var cache)) cache.Dispose();

        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if (ext.Length is < 2 or > 5) ext = "";
        return new Uri($"http://127.0.0.1:{_port}/{token}{ext}");
    }

    private async Task AcceptLoop()
    {
        while (_listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleAsync(client));
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Logger.Error("代理连接异常", ex); }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            try
            {
                var head = await ReadHeadAsync(stream);
                if (head == null) return;
                var lines = head.Split("\r\n");
                var first = lines[0].Split(' ');
                if (first.Length < 2) return;
                var method = first[0];
                var token = first[1].TrimStart('/').Split('.', '?')[0];
                string? range = lines.Skip(1)
                    .Select(l => l.Split(':', 2))
                    .Where(p => p.Length == 2 && p[0].Trim().Equals("Range", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p[1].Trim()).FirstOrDefault();

                if (!_map.TryGetValue(token, out var target))
                {
                    await WriteAsync(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    return;
                }

                // 已经在后台整文件下载的，直接从临时文件提供
                if (_caches.TryGetValue(token, out var existing))
                {
                    await ServeFromCacheAsync(stream, existing, method, range);
                    return;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
                request.Headers.TryAddWithoutValidation("User-Agent", HttpService.UserAgent(false));
                request.Headers.TryAddWithoutValidation("Accept", "*/*");
                if (range != null) request.Headers.TryAddWithoutValidation("Range", range);
                if (target.Headers != null)
                {
                    foreach (var (k, v) in target.Headers)
                    {
                        request.Headers.Remove(k);
                        request.Headers.TryAddWithoutValidation(k, v);
                    }
                }

                var response = await HttpService.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var contentType = response.Content.Headers.ContentType?.ToString();
                if (string.IsNullOrEmpty(contentType) || contentType.StartsWith("application/octet-stream") || contentType.StartsWith("text/"))
                    contentType = GuessContentType(target.Url) ?? contentType ?? "audio/mpeg";
                var length = response.Content.Headers.ContentLength;

                // 服务器不支持 Range（返回 200 且未声明 Accept-Ranges）：转为后台整文件下载
                var supportsRange = response.StatusCode == HttpStatusCode.PartialContent ||
                                    response.Headers.AcceptRanges.Contains("bytes");
                if (response.StatusCode == HttpStatusCode.OK && !supportsRange && length is > 0)
                {
                    var cache = _caches.GetOrAdd(token, _ => new DownloadCache(Path.Combine(_cacheDir, token + ".tmp"), length.Value, contentType));
                    cache.StartDownload(response); // 接管 response，负责释放
                    await ServeFromCacheAsync(stream, cache, method, range);
                    return;
                }

                using (response)
                {
                    var sb = new StringBuilder();
                    sb.Append($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n");
                    sb.Append($"Content-Type: {contentType}\r\n");
                    if (length is { } len) sb.Append($"Content-Length: {len}\r\n");
                    if (response.Content.Headers.ContentRange is { } cr) sb.Append($"Content-Range: {cr}\r\n");
                    sb.Append("Accept-Ranges: bytes\r\nConnection: close\r\n\r\n");
                    await WriteAsync(stream, sb.ToString());

                    if (method != "HEAD")
                    {
                        await using var body = await response.Content.ReadAsStreamAsync();
                        await body.CopyToAsync(stream, 64 * 1024);
                    }
                }
            }
            catch (IOException) { /* 播放器主动断开（拖动进度等）属于正常情况 */ }
            catch (SocketException) { }
            catch (Exception ex)
            {
                Logger.Error("音频代理转发失败", ex);
                try { await WriteAsync(stream, "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); } catch { }
            }
        }
    }

    /// <summary>按 Range 从（可能仍在下载中的）临时文件提供数据，未下载到的部分等待后台下载</summary>
    private static async Task ServeFromCacheAsync(NetworkStream stream, DownloadCache cache, string method, string? range)
    {
        var total = cache.Total;
        long start = 0, end = total - 1;
        var partial = TryParseRange(range, total, out start, out end);
        if (!partial) { start = 0; end = total - 1; }

        var sb = new StringBuilder();
        sb.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
        sb.Append($"Content-Type: {cache.ContentType}\r\n");
        sb.Append($"Content-Length: {end - start + 1}\r\n");
        if (partial) sb.Append($"Content-Range: bytes {start}-{end}/{total}\r\n");
        sb.Append("Accept-Ranges: bytes\r\nConnection: close\r\n\r\n");
        await WriteAsync(stream, sb.ToString());
        if (method == "HEAD") return;

        await using var file = new FileStream(cache.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
        var buffer = new byte[64 * 1024];
        var pos = start;
        while (pos <= end)
        {
            var available = await cache.WaitForAsync(pos);
            if (available <= pos) break; // 下载失败或已取消
            file.Position = pos;
            var toRead = (int)Math.Min(buffer.Length, Math.Min(available, end + 1) - pos);
            var n = await file.ReadAsync(buffer.AsMemory(0, toRead));
            if (n <= 0) break;
            await stream.WriteAsync(buffer.AsMemory(0, n));
            pos += n;
        }
    }

    /// <summary>解析 “bytes=a-b” / “bytes=a-”</summary>
    private static bool TryParseRange(string? range, long total, out long start, out long end)
    {
        start = 0;
        end = total - 1;
        if (range == null || !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var spec = range[6..].Split(',')[0].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0) return false;
        var a = spec[..dash];
        var b = spec[(dash + 1)..];
        if (a.Length == 0)
        {
            // bytes=-500：最后 500 字节
            if (!long.TryParse(b, out var suffix)) return false;
            start = Math.Max(0, total - suffix);
            return true;
        }
        if (!long.TryParse(a, out start) || start >= total) return false;
        if (b.Length > 0 && long.TryParse(b, out var e)) end = Math.Min(e, total - 1);
        return true;
    }

    private void CleanupCacheDir()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_cacheDir, "*.tmp")) File.Delete(f);
        }
        catch { }
    }

    private static string? GuessContentType(string url)
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" or ".m4b" or ".mp4" => "audio/mp4",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".wma" => "audio/x-ms-wma",
            ".ogg" or ".opus" => "audio/ogg",
            _ => null,
        };
    }

    private static async Task<string?> ReadHeadAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (n == 0) return null;
            total += n;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) return text[..end];
        }
        return null;
    }

    private static Task WriteAsync(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        return stream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>后台把整个音频文件下载到临时文件，供多次 Range 请求读取</summary>
    private sealed class DownloadCache(string path, long total, string contentType) : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly object _lock = new();
        private TaskCompletionSource _progress = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _written;
        private bool _finished;
        private int _started;

        public string Path { get; } = path;
        public long Total { get; } = total;
        public string ContentType { get; } = contentType;

        public void StartDownload(HttpResponseMessage response)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                response.Dispose();
                return;
            }
            // 先创建文件，读取方才能打开
            using (new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
            _ = Task.Run(async () =>
            {
                try
                {
                    using (response)
                    await using (var body = await response.Content.ReadAsStreamAsync(_cts.Token))
                    await using (var file = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true))
                    {
                        var buffer = new byte[64 * 1024];
                        int n;
                        while ((n = await body.ReadAsync(buffer, _cts.Token)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, n), _cts.Token);
                            await file.FlushAsync(_cts.Token);
                            Signal(n);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.Error("音频后台下载失败", ex);
                }
                catch (OperationCanceledException) { }
                finally
                {
                    lock (_lock)
                    {
                        _finished = true;
                        _progress.TrySetResult();
                    }
                }
            });
        }

        private void Signal(int bytes)
        {
            lock (_lock)
            {
                _written += bytes;
                var old = _progress;
                _progress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                old.TrySetResult();
            }
        }

        /// <summary>等待直到 position 之后有数据可读，返回当前已下载的字节数</summary>
        public async Task<long> WaitForAsync(long position)
        {
            while (true)
            {
                Task wait;
                lock (_lock)
                {
                    if (_written > position || _finished) return _written;
                    wait = _progress.Task;
                }
                await wait;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { File.Delete(Path); } catch { }
        }
    }
}
