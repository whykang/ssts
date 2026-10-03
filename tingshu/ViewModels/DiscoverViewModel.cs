using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TingShu.Sdk;
using tingshu.Core;

namespace tingshu.ViewModels;

public sealed partial class DiscoverViewModel : ObservableObject, IPageActivated
{
    private CancellationTokenSource? _menuCts;
    private CancellationTokenSource? _listCts;
    private string? _nextUrl;

    public ObservableCollection<SourceEntry> Sources { get; } = new();
    public ObservableCollection<CategoryMenu> Menus { get; } = new();
    public ObservableCollection<CategoryTab> Tabs { get; } = new();
    public ObservableCollection<BookItem> Books { get; } = new();

    [ObservableProperty] private SourceEntry? _selectedSource;
    [ObservableProperty] private CategoryMenu? _selectedMenu;
    [ObservableProperty] private CategoryTab? _selectedTab;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))] private bool _isLoading;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))] private bool _isLoadingMore;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))] private bool _hasMore;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _pageInfo;

    public DiscoverViewModel()
    {
        PluginManager.Instance.Changed += () => System.Windows.Application.Current.Dispatcher.InvokeAsync(RefreshSources);
    }

    public void OnActivated()
    {
        if (Sources.Count == 0) RefreshSources();
    }

    private void RefreshSources()
    {
        var current = SelectedSource?.Id ?? AppSettings.Current.LastDiscoverSource;
        var list = PluginManager.Instance.Enabled.Where(e => e.Source.IsDiscoverable).ToList();
        if (list.Select(e => e.Id).SequenceEqual(Sources.Select(e => e.Id))) return;
        Sources.Clear();
        foreach (var e in list) Sources.Add(e);
        SelectedSource = Sources.FirstOrDefault(s => s.Id == current) ?? Sources.FirstOrDefault();
        if (SelectedSource == null)
        {
            Menus.Clear();
            Tabs.Clear();
            Books.Clear();
        }
    }

    partial void OnSelectedSourceChanged(SourceEntry? value)
    {
        Menus.Clear();
        Tabs.Clear();
        Books.Clear();
        Error = null;
        if (value == null) return;
        AppSettings.Current.LastDiscoverSource = value.Id;
        AppSettings.Current.Save();
        _ = LoadMenusAsync(value);
    }

    private async Task LoadMenusAsync(SourceEntry entry)
    {
        _menuCts?.Cancel();
        var cts = _menuCts = new CancellationTokenSource();
        IsLoading = true;
        try
        {
            var menus = await Task.Run(() => entry.Source.GetCategoryMenusAsync(cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            foreach (var m in menus.Where(m => m.Tabs.Count > 0)) Menus.Add(m);
            SelectedMenu = Menus.FirstOrDefault();
            if (Menus.Count == 0)
            {
                IsLoading = false;
                Error = "这个源没有提供分类";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            IsLoading = false;
            Error = "加载分类失败：" + ex.Message;
        }
    }

    partial void OnSelectedMenuChanged(CategoryMenu? value)
    {
        Tabs.Clear();
        if (value == null) return;
        foreach (var t in value.Tabs) Tabs.Add(t);
        SelectedTab = Tabs.FirstOrDefault();
    }

    partial void OnSelectedTabChanged(CategoryTab? value)
    {
        Books.Clear();
        if (value != null) _ = LoadPageAsync(value.Url, reset: true);
    }

    [RelayCommand]
    private Task Refresh() => SelectedTab == null ? Task.CompletedTask : LoadPageAsync(SelectedTab.Url, reset: true);

    private bool CanLoadMore() => HasMore && !IsLoadingMore && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMore() => _nextUrl == null ? Task.CompletedTask : LoadPageAsync(_nextUrl, reset: false);

    private async Task LoadPageAsync(string url, bool reset)
    {
        var entry = SelectedSource;
        if (entry == null) return;
        if (reset)
        {
            _listCts?.Cancel();
            Books.Clear();
            HasMore = false;
            IsLoading = true;
        }
        else IsLoadingMore = true;
        var cts = _listCts = reset ? new CancellationTokenSource() : _listCts ?? new CancellationTokenSource();
        Error = null;
        try
        {
            var page = await Task.Run(() => entry.Source.GetCategoryPageAsync(url, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            var existing = Books.Select(b => b.Book.BookUrl).ToHashSet();
            foreach (var b in page.Books)
            {
                b.SourceId = entry.Id;
                if (existing.Add(b.BookUrl)) Books.Add(new BookItem(b));
            }
            _nextUrl = page.NextUrl;
            HasMore = page.HasMore;
            PageInfo = page.TotalPage > 1 ? $"{page.CurrentPage} / {page.TotalPage}" : null;
            if (Books.Count == 0) Error = "这个分类暂时没有内容";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error($"加载分类失败 {url}", ex);
            Error = "加载失败：" + ex.Message;
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsLoading = false;
                IsLoadingMore = false;
            }
        }
    }
}
