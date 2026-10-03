using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TingShu.Sdk;
using tingshu.Core;
using tingshu.Views;

namespace tingshu.ViewModels;

public sealed partial class PluginsViewModel : ObservableObject, IPageActivated
{
    public ObservableCollection<SourceEntry> Entries { get; } = new();
    public ObservableCollection<PluginLoadError> Errors { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _showLogs;
    [ObservableProperty] private string _summary = "";

    public string UserPluginsDir => AppPaths.UserPluginsDir;

    public PluginsViewModel()
    {
        PluginManager.Instance.Changed += () => Application.Current.Dispatcher.InvokeAsync(Refresh);
        Logger.Logged += line => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 这里出错不能再写日志，否则会形成循环
            try
            {
                Logs.Add(line);
                while (Logs.Count > 300) Logs.RemoveAt(0);
            }
            catch { }
        });
    }

    public void OnActivated() => Refresh();

    partial void OnFilterChanged(string value) => Refresh();

    private void Refresh()
    {
        var f = Filter.Trim();
        Entries.Clear();
        foreach (var e in PluginManager.Instance.All.Where(e => f.Length == 0 || e.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            Entries.Add(e);
        Errors.Clear();
        foreach (var e in PluginManager.Instance.Errors) Errors.Add(e);
        var all = PluginManager.Instance.All;
        Summary = all.Count == 0
            ? "还没有导入插件"
            : $"共 {all.Count} 个源，已启用 {all.Count(e => e.IsEnabled)} 个 · 来自 {all.Select(e => e.FilePath).Distinct().Count()} 个插件";
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入插件",
            Filter = "DLL 插件 (*.dll;*.zip)|*.dll;*.zip",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;
        var ok = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                PluginManager.Instance.Import(file);
                ok++;
            }
            catch (Exception ex)
            {
                MainViewModel.Instance.ShowToast($"导入 {Path.GetFileName(file)} 失败：{ex.Message}");
            }
        }
        if (ok > 0) Reload($"成功导入 {ok} 个文件");
    }

    [RelayCommand]
    private async Task ImportUrl()
    {
        var url = InputDialog.Show("从网址导入", "输入插件 .dll 或插件包 .zip 的下载地址", "https://");
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out _)) return;
        try
        {
            MainViewModel.Instance.ShowToast("正在下载…");
            await PluginManager.Instance.ImportFromUrlAsync(url.Trim());
            Reload("导入成功");
        }
        catch (Exception ex)
        {
            MainViewModel.Instance.ShowToast("导入失败：" + ex.Message);
        }
    }

    private void Reload(string message)
    {
        PluginManager.Instance.LoadAll();
        MainViewModel.Instance.ShowToast(message);
    }

    [RelayCommand]
    private void ReloadAll() => Reload("已重新加载（替换已加载的 DLL 需要重启程序）");

    [RelayCommand]
    private void OpenFolder() => Open(AppPaths.UserPluginsDir);

    private const string DocsUrl = "https://github.com/whykang/ssts/blob/main/docs/plugin-development.md";

    [RelayCommand]
    private void OpenDocs() => Open(DocsUrl);

    [RelayCommand]
    private void Delete(SourceEntry? entry)
    {
        if (entry == null) return;
        var siblings = PluginManager.Instance.All.Where(e => e.FilePath == entry.FilePath).Select(e => e.Name).ToList();
        var message = siblings.Count > 1
            ? $"“{entry.Name}”所在的插件 {Path.GetFileName(entry.FilePath)} 共包含 {siblings.Count} 个源，删除会移除整个插件：\n{string.Join("、", siblings)}\n\n只想停用这个源的话，关掉它的开关即可。确定删除整个插件吗？"
            : $"确定删除“{entry.Name}”吗？";
        if (MessageBox.Show(Application.Current.MainWindow!, message, "删除插件", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        try
        {
            PluginManager.Instance.Delete(entry);
            MainViewModel.Instance.ShowToast("已删除，重启后彻底移除");
        }
        catch (Exception ex)
        {
            MainViewModel.Instance.ShowToast(ex.Message);
        }
    }

    [RelayCommand]
    private void Login(SourceEntry? entry)
    {
        if (entry?.Source is not ILoginSource login || string.IsNullOrEmpty(login.LoginUrl)) return;
        new LoginWindow(login.LoginUrl, login.LoginDesktop, entry.Name) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private void Configure(SourceEntry? entry)
    {
        if (entry?.Source is not IConfigurableSource cfg) return;
        new ConfigWindow(entry, cfg.GetConfigItems()) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private void MoveUp(SourceEntry? entry) => MoveBy(entry, -1);

    [RelayCommand]
    private void MoveDown(SourceEntry? entry) => MoveBy(entry, 1);

    private void MoveBy(SourceEntry? entry, int delta)
    {
        if (entry == null) return;
        var ids = PluginManager.Instance.All.Select(e => e.Id).ToList();
        var i = ids.IndexOf(entry.Id);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= ids.Count) return;
        (ids[i], ids[j]) = (ids[j], ids[i]);
        PluginManager.Instance.SaveOrder(ids);
        PluginManager.Instance.LoadAll();
    }

    /// <summary>快速测试：搜索一个关键词或加载第一个分类</summary>
    [RelayCommand]
    private async Task Test(SourceEntry? entry)
    {
        if (entry == null) return;
        ShowLogs = true;
        var s = entry.Source;
        Logger.Info($"—— 开始测试 {s.Name} ——");
        try
        {
            var sw = Stopwatch.StartNew();
            if (s.IsDiscoverable)
            {
                var menus = await Task.Run(() => s.GetCategoryMenusAsync(CancellationToken.None));
                Logger.Info($"分类：{menus.Count} 个大类");
                var tab = menus.SelectMany(m => m.Tabs).FirstOrDefault();
                if (tab != null)
                {
                    var page = await Task.Run(() => s.GetCategoryPageAsync(tab.Url, CancellationToken.None));
                    Logger.Info($"“{tab.Title}”：{page.Books.Count} 本书，第 {page.CurrentPage}/{page.TotalPage} 页，下一页 {page.NextUrl ?? "无"}");
                    var book = page.Books.FirstOrDefault();
                    if (book != null) await TestBook(s, book);
                }
            }
            else if (s.IsSearchable)
            {
                var result = await Task.Run(() => s.SearchAsync("我", 1, CancellationToken.None));
                Logger.Info($"搜索“我”：{result.Books.Count} 个结果，共 {result.TotalPage} 页");
                var book = result.Books.FirstOrDefault();
                if (book != null) await TestBook(s, book);
            }
            Logger.Info($"—— 测试完成，用时 {sw.Elapsed.TotalSeconds:0.0}s ——");
        }
        catch (Exception ex)
        {
            Logger.Error($"测试失败", ex);
        }
    }

    private static async Task TestBook(SourceBase s, Book book)
    {
        Logger.Info($"书籍：{book.Title} | {book.BookUrl}");
        var detail = await Task.Run(() => s.GetBookDetailAsync(book.BookUrl, true, false, CancellationToken.None));
        Logger.Info($"章节：{detail.Episodes.Count} 个");
        var ep = detail.Episodes.FirstOrDefault(e => e.IsFree);
        if (ep == null) return;
        var entry = PluginManager.Instance.Find(s.Id)!;
        var (url, _) = await AudioResolver.ResolveAsync(entry, ep, CancellationToken.None);
        Logger.Info($"音频：{url}");
    }

    [RelayCommand]
    private void ClearLogs() => Logs.Clear();

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MainViewModel.Instance.ShowToast("无法打开：" + ex.Message); }
    }
}
