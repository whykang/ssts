using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using CommunityToolkit.Mvvm.ComponentModel;
using TingShu.Sdk;

namespace tingshu.Core;

/// <summary>一个已加载的源</summary>
public partial class SourceEntry : ObservableObject
{
    public SourceEntry(SourceBase source, string filePath, string? version, string? author)
    {
        Source = source;
        FilePath = filePath;
        Version = version;
        Author = author;
        var s = AppSettings.Current;
        _isEnabled = s.EnabledSources.Contains(source.Id) || (!s.DisabledSources.Contains(source.Id) && source.EnabledByDefault);
    }

    public SourceBase Source { get; }
    public string FilePath { get; }
    public string? Version { get; }
    public string? Author { get; }

    public string Id => Source.Id;
    public string Name => Source.Name;
    /// <summary>所在插件的名字（DLL 文件名）</summary>
    public string PluginName => Path.GetFileNameWithoutExtension(FilePath);
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1];
    public bool CanLogin => Source is ILoginSource login && !string.IsNullOrEmpty(login.LoginUrl);
    public bool CanConfigure => Source is IConfigurableSource;

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        var s = AppSettings.Current;
        if (value)
        {
            s.DisabledSources.Remove(Id);
            s.EnabledSources.Add(Id);
        }
        else
        {
            s.EnabledSources.Remove(Id);
            s.DisabledSources.Add(Id);
        }
        AppSettings.Current.Save();
        PluginManager.Instance.NotifyChanged();
    }
}

public record PluginLoadError(string FilePath, string Message);

/// <summary>
/// 插件加载：只支持 DLL 插件（继承 SourceBase 的 .NET 程序集）。
/// 程序本身不带任何源，全部来自用户导入到数据目录 plugins 下的插件：
/// plugins/&lt;名字&gt;/&lt;名字&gt;.dll 或 plugins/*.dll。
/// </summary>
public sealed class PluginManager
{
    public static PluginManager Instance { get; } = new();

    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "TingShu.Sdk", "AngleSharp",
    };

    private readonly List<SourceEntry> _entries = new();

    public IReadOnlyList<SourceEntry> All => _entries;
    public IEnumerable<SourceEntry> Enabled => _entries.Where(e => e.IsEnabled);
    public List<PluginLoadError> Errors { get; } = new();

    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    public SourceEntry? Find(string? sourceId) => sourceId == null ? null : _entries.FirstOrDefault(e => e.Id == sourceId);

    public void LoadAll()
    {
        _entries.Clear();
        Errors.Clear();
        var map = new Dictionary<string, SourceEntry>();
        foreach (var entry in LoadDirectory(AppPaths.UserPluginsDir)) map[entry.Id] = entry;

        var order = AppSettings.Current.SourceOrder;
        _entries.AddRange(map.Values
            .OrderBy(e => order.IndexOf(e.Id) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(e => e.Name, StringComparer.CurrentCulture));
        Logger.Info($"已加载 {_entries.Count} 个源，{Errors.Count} 个错误");
        NotifyChanged();
    }

    private IEnumerable<SourceEntry> LoadDirectory(string dir)
    {
        var result = new List<SourceEntry>();
        // DLL：根目录下的 dll + 子目录中与目录同名的 dll（其余 dll 视为依赖）
        var dlls = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).ToList();
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var main = Path.Combine(sub, Path.GetFileName(sub) + ".dll");
            if (File.Exists(main)) dlls.Add(main);
        }
        foreach (var dll in dlls)
        {
            if (SharedAssemblies.Contains(Path.GetFileNameWithoutExtension(dll))) continue;
            result.AddRange(LoadDll(dll));
        }
        return result;
    }

    private IEnumerable<SourceEntry> LoadDll(string path)
    {
        var list = new List<SourceEntry>();
        try
        {
            // 每次加载都用新的上下文，并从内存加载（不锁定文件），这样导入新版后“重新加载”即可生效
            var ctx = new PluginLoadContext(path);
            var asm = ctx.LoadFromBytes(path);
            var version = asm.GetName().Version?.ToString(3);
            var author = asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            IEnumerable<SourceBase> sources;
            var provider = types.FirstOrDefault(t => typeof(ISourceProvider).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);
            if (provider != null)
            {
                sources = ((ISourceProvider)Activator.CreateInstance(provider)!).GetSources().ToList();
            }
            else
            {
                sources = types
                    .Where(t => typeof(SourceBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic && t.GetConstructor(Type.EmptyTypes) != null)
                    .Select(t => (SourceBase)Activator.CreateInstance(t)!)
                    .ToList();
            }

            foreach (var s in sources)
            {
                s.Initialize(new SourceHost(s.Id, s.Name));
                if (s is ICertificateNameMismatchHosts cert) HttpService.AllowNameMismatch(cert.NameMismatchHosts);
                list.Add(new SourceEntry(s, path, version, author));
            }
            if (list.Count == 0) Errors.Add(new PluginLoadError(path, "程序集中没有找到继承 SourceBase 的公开类"));
        }
        catch (Exception ex)
        {
            Logger.Error($"加载插件 {path} 失败", ex);
            Errors.Add(new PluginLoadError(path, ex.Message));
        }
        return list;
    }

    public void SaveOrder(IEnumerable<string> ids)
    {
        AppSettings.Current.SourceOrder = ids.ToList();
        AppSettings.Current.Save();
    }

    /// <summary>导入插件文件（.dll / .zip），返回导入后的路径</summary>
    public string Import(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(file);
        switch (ext)
        {
            case ".dll":
                var dllDir = AppPaths.Ensure(Path.Combine(AppPaths.UserPluginsDir, name));
                var dllTarget = Path.Combine(dllDir, Path.GetFileName(file));
                File.Copy(file, dllTarget, true);
                return dllTarget;
            case ".zip":
                var zipDir = Path.Combine(AppPaths.UserPluginsDir, name);
                using (var zip = ZipFile.OpenRead(file))
                {
                    // zip 里只有一个顶层目录时，去掉这一层
                    var roots = zip.Entries.Select(e => e.FullName.Split('/', '\\')[0]).Distinct().ToList();
                    var strip = roots.Count == 1 && zip.Entries.Any(e => e.FullName.Contains('/')) ? roots[0] + "/" : "";
                    if (strip.Length > 0) zipDir = Path.Combine(AppPaths.UserPluginsDir, roots[0]);
                    Directory.CreateDirectory(zipDir);
                    foreach (var e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        var rel = e.FullName.Replace('\\', '/');
                        if (strip.Length > 0 && rel.StartsWith(strip)) rel = rel[strip.Length..];
                        var target = Path.GetFullPath(Path.Combine(zipDir, rel));
                        if (!target.StartsWith(Path.GetFullPath(zipDir), StringComparison.OrdinalIgnoreCase)) continue; // zip slip
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        e.ExtractToFile(target, true);
                    }
                }
                return zipDir;
            default:
                throw new NotSupportedException("只支持 .dll 插件或 .zip 插件包");
        }
    }

    public async Task<string> ImportFromUrlAsync(string url, CancellationToken ct = default)
    {
        using var response = await HttpService.Client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (ext is not (".dll" or ".zip"))
        {
            // 根据内容判断
            ext = bytes.Length > 1 && bytes[0] == 'P' && bytes[1] == 'K' ? ".zip"
                : bytes.Length > 1 && bytes[0] == 'M' && bytes[1] == 'Z' ? ".dll"
                : throw new NotSupportedException("下载的文件不是 .dll 插件或 .zip 插件包");
            fileName = (string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(fileName)) ? "imported" : Path.GetFileNameWithoutExtension(fileName)) + ext;
        }
        var tmp = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        try { return Import(tmp); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    public void Delete(SourceEntry entry)
    {
        // 标记后在下次启动时删除（一个 DLL 包含多个源时，删除其中任意一个即删除整个插件）
        File.WriteAllText(entry.FilePath + ".delete", "");
        _entries.RemoveAll(e => e.FilePath == entry.FilePath);
        NotifyChanged();
    }

    /// <summary>启动时清理标记删除的 DLL</summary>
    public static void CleanupPendingDeletes()
    {
        try
        {
            foreach (var marker in Directory.GetFiles(AppPaths.UserPluginsDir, "*.delete", SearchOption.AllDirectories))
            {
                var dll = marker[..^".delete".Length];
                var dir = Path.GetDirectoryName(dll)!;
                if (Path.GetFileName(dir) == Path.GetFileNameWithoutExtension(dll) && dir != AppPaths.UserPluginsDir)
                    Directory.Delete(dir, true);
                else
                {
                    File.Delete(dll);
                    File.Delete(marker);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("清理插件失败", ex);
        }
    }
}

/// <summary>
/// 插件程序集加载上下文：SDK 与 AngleSharp 与宿主共享，其余依赖从插件目录加载
/// </summary>
internal sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(pluginPath))
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);
    private readonly string _dir = Path.GetDirectoryName(pluginPath)!;

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is "TingShu.Sdk" or "AngleSharp" || name.Name!.StartsWith("System.") || name.Name.StartsWith("Microsoft."))
        {
            // 宿主中已有则共享
            var shared = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
            if (shared != null) return shared;
            try { return Default.LoadFromAssemblyName(name); }
            catch { /* 宿主没有，继续从插件目录加载 */ }
        }
        var path = _resolver.ResolveAssemblyToPath(name);
        if (path == null)
        {
            var candidate = Path.Combine(_dir, name.Name + ".dll");
            if (File.Exists(candidate)) path = candidate;
        }
        return path != null ? LoadFromBytes(path) : null;
    }

    /// <summary>读入内存后加载，不锁定磁盘上的 dll（导入新版时可以直接覆盖）</summary>
    public Assembly LoadFromBytes(string path)
    {
        using var dll = new MemoryStream(File.ReadAllBytes(path));
        var pdbPath = Path.ChangeExtension(path, ".pdb");
        if (!File.Exists(pdbPath)) return LoadFromStream(dll);
        using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath));
        return LoadFromStream(dll, pdb);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
