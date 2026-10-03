using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace tingshu.Core;

public static class AppPaths
{
    public static string AppDir { get; } = AppContext.BaseDirectory;

    /// <summary>程序目录下有 portable.txt 时，数据保存在程序目录（便携模式）</summary>
    public static string DataDir { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"))
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TingShu");

    public static string UserPluginsDir => Ensure(Path.Combine(DataDir, "plugins"));
    public static string CacheDir => Ensure(Path.Combine(DataDir, "cache"));
    public static string WebViewDataDir => Ensure(Path.Combine(DataDir, "webview"));
    public static string SettingsFile => Path.Combine(Ensure(DataDir), "settings.json");
    public static string LibraryFile => Path.Combine(Ensure(DataDir), "library.json");
    public static string LogFile => Path.Combine(Ensure(DataDir), "log.txt");

    public static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception ex)
        {
            Logger.Error($"读取 {path} 失败", ex);
            try { File.Copy(path, path + ".bak", true); } catch { }
        }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            Logger.Error($"保存 {path} 失败", ex);
        }
    }
}

public static class Logger
{
    private static readonly object Lock = new();
    public static event Action<string>? Logged;

    public static void Info(string msg) => Write("INFO", msg);

    public static void Error(string msg, Exception? ex = null)
    {
        if (ex is AggregateException { InnerExceptions.Count: > 0 } agg) ex = agg.Flatten().InnerExceptions[0];
        Write("ERROR", ex == null ? msg : $"{msg}: {ex.GetType().Name}: {ex.Message}");
    }

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        Logged?.Invoke(line);
        lock (Lock)
        {
            try
            {
                var fi = new FileInfo(AppPaths.LogFile);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
