using TingShu.Sdk;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using tingshu.Core;

namespace tingshu.ViewModels;

public enum SearchStatus { Waiting, Loading, Done, Empty, Failed, NeedsVerification }

/// <summary>某个源的搜索结果</summary>
public sealed partial class SourceSearchState(SourceEntry entry) : ObservableObject
{
    public SourceEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public List<BookItem> Books { get; } = new();
    public int Page { get; set; } = 1;
    public int TotalPage { get; set; } = 1;

    [ObservableProperty] private SearchStatus _status = SearchStatus.Waiting;
    [ObservableProperty] private int _count;
    [ObservableProperty] private string? _error;

    public bool HasMore => Page < TotalPage;
    public bool NeedsVerification => Status == SearchStatus.NeedsVerification;

    partial void OnStatusChanged(SearchStatus value) => OnPropertyChanged(nameof(NeedsVerification));
}

/// <summary>筛选里的“全部”。不能用 null 代替：ListBox 无法点选 null 项</summary>
public sealed partial class AllSearchFilter : ObservableObject
{
    public string Name => "全部";
    [ObservableProperty] private int _count;
}

public sealed partial class SearchViewModel : ObservableObject
{
    private readonly AllSearchFilter _all = new();

    private CancellationTokenSource? _cts;

    public ObservableCollection<string> History { get; } = new(AppSettings.Current.SearchHistory);
    public ObservableCollection<SourceSearchState> SourceStates { get; } = new();
    /// <summary>筛选：第一个为“全部”，其后为有结果的源</summary>
    public ObservableCollection<object> Filters { get; } = new();
    public ObservableCollection<BookItem> Results { get; } = new();

    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))] private bool _isSearching;
    [ObservableProperty] private bool _hasSearched;
    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private SourceSearchState? _selectedFilter;
    [ObservableProperty] private int _selectedFilterIndex;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))] private bool _isLoadingMore;

    [RelayCommand]
    private async Task Search()
    {
        var keyword = Keyword.Trim();
        if (keyword.Length == 0) return;
        AddHistory(keyword);

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        SourceStates.Clear();
        Filters.Clear();
        _all.Count = 0;
        Filters.Add(_all);
        Results.Clear();
        SelectedFilterIndex = 0;
        RefreshHasMore();
        HasSearched = true;
        IsSearching = true;

        var sources = PluginManager.Instance.Enabled.Where(e => e.Source.IsSearchable).ToList();
        if (sources.Count == 0)
        {
            IsSearching = false;
            ProgressText = "没有可搜索的源，请在“插件”中启用或导入";
            return;
        }
        foreach (var s in sources) SourceStates.Add(new SourceSearchState(s));

        var settings = AppSettings.Current;
        using var throttle = new SemaphoreSlim(Math.Max(1, settings.SearchConcurrency));
        var done = 0;
        UpdateProgress(done, sources.Count);

        var tasks = SourceStates.ToList().Select(async state =>
        {
            await throttle.WaitAsync(cts.Token);
            try
            {
                state.Status = SearchStatus.Loading;
                await SearchSourceAsync(state, keyword, 1, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            finally
            {
                throttle.Release();
                if (!cts.IsCancellationRequested) UpdateProgress(++done, sources.Count);
            }
        }).ToList();

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
        if (cts.IsCancellationRequested) return;
        IsSearching = false;
        RefreshHasMore();
        var ok = SourceStates.Count(s => s.Count > 0);
        var failed = SourceStates.Count(s => s.Status == SearchStatus.Failed);
        ProgressText = $"共 {Results.Count} 个结果，来自 {ok} 个源" + (failed > 0 ? $"，{failed} 个源失败" : "");
    }

    private void UpdateProgress(int done, int total) =>
        ProgressText = done < total ? $"正在搜索… {done}/{total}" : null;

    private async Task SearchSourceAsync(SourceSearchState state, string keyword, int page, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(AppSettings.Current.SearchTimeoutSeconds));
        try
        {
            var result = await Task.Run(() => state.Entry.Source.SearchAsync(keyword, page, timeout.Token), timeout.Token);
            if (ct.IsCancellationRequested) return;
            state.Page = page;
            state.TotalPage = result.TotalPage;
            var items = result.Books.Select(b =>
            {
                b.SourceId = state.Entry.Id;
                return new BookItem(b);
            }).ToList();
            state.Books.AddRange(items);
            state.Count = state.Books.Count;
            _all.Count = SourceStates.Sum(s => s.Count);
            state.Status = state.Count > 0 ? SearchStatus.Done
                : state.Entry.Source is ISearchVerification ? SearchStatus.NeedsVerification
                : SearchStatus.Empty;
            if (state.Count > 0 && !Filters.Contains(state)) Filters.Add(state);
            if (SelectedFilter == null || SelectedFilter == state)
                foreach (var i in items) Results.Add(i);
            RefreshHasMore();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            state.Status = SearchStatus.Failed;
            state.Error = "超时";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.Status = state.Entry.Source is ISearchVerification ? SearchStatus.NeedsVerification : SearchStatus.Failed;
            state.Error = ex.Message;
            Logger.Error($"[{state.Name}] 搜索失败", ex);
        }
    }

    partial void OnSelectedFilterIndexChanged(int value)
    {
        SelectedFilter = value > 0 && value < Filters.Count ? Filters[value] as SourceSearchState : null;
        Results.Clear();
        var source = SelectedFilter == null ? SourceStates.SelectMany(s => s.Books) : SelectedFilter.Books;
        foreach (var b in source) Results.Add(b);
        RefreshHasMore();
    }

    /// <summary>“全部”时：任意一个有结果的源还有下一页；选中某个源时：这个源还有下一页</summary>
    public bool HasMore => !IsSearching && (SelectedFilter?.HasMore ?? SourceStates.Any(s => s.Count > 0 && s.HasMore));

    private void RefreshHasMore()
    {
        OnPropertyChanged(nameof(HasMore));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(HasMore));

    private bool CanLoadMore() => HasMore && !IsLoadingMore;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMore()
    {
        if (_cts == null) return;
        var ct = _cts.Token;
        var keyword = Keyword.Trim();
        var states = SelectedFilter != null
            ? new List<SourceSearchState> { SelectedFilter }
            : SourceStates.Where(s => s.Count > 0 && s.HasMore).ToList();
        if (states.Count == 0) return;
        IsLoadingMore = true;
        try
        {
            using var throttle = new SemaphoreSlim(Math.Max(1, AppSettings.Current.SearchConcurrency));
            await Task.WhenAll(states.Select(async s =>
            {
                await throttle.WaitAsync(ct);
                try { await SearchSourceAsync(s, keyword, s.Page + 1, ct); }
                finally { throttle.Release(); }
            }));
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsLoadingMore = false;
            RefreshHasMore();
            if (!ct.IsCancellationRequested) ProgressText = $"共 {_all.Count} 个结果，来自 {SourceStates.Count(s => s.Count > 0)} 个源";
        }
    }

    /// <summary>打开网站的搜索验证页面，验证完成后重新搜索这个源</summary>
    [RelayCommand]
    private async Task Verify(SourceSearchState? state)
    {
        if (state?.Entry.Source is not ISearchVerification v || _cts == null) return;
        var keyword = Keyword.Trim();
        var ct = _cts.Token;
        // 验证通过（页面不再是验证页）后窗口自动关闭
        new Views.LoginWindow(v.GetSearchVerificationUrl(keyword), v.SearchVerificationDesktop, state.Name, "搜索验证", v.IsVerificationPassed)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        }.ShowDialog();

        foreach (var old in state.Books) Results.Remove(old);
        state.Books.Clear();
        state.Count = 0;
        state.Error = null;
        state.Status = SearchStatus.Loading;
        try
        {
            // 提交验证码本身就算一次搜索，网站限制两次搜索的间隔：先等一会儿，仍未成功再自动重试一次
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await CountdownAsync(state.Name, Math.Max(v.SearchDelaySeconds, attempt == 0 ? 0 : 5), ct);
                state.Status = SearchStatus.Loading;
                await SearchSourceAsync(state, keyword, 1, ct);
                if (state.Status != SearchStatus.NeedsVerification) break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested) ProgressText = $"共 {Results.Count} 个结果，来自 {SourceStates.Count(s => s.Count > 0)} 个源";
        }
    }

    private async Task CountdownAsync(string name, int seconds, CancellationToken ct)
    {
        for (var i = seconds; i > 0; i--)
        {
            ProgressText = $"“{name}”验证完成，网站限制搜索间隔，{i} 秒后自动重新搜索…";
            await Task.Delay(1000, ct);
        }
        ProgressText = $"正在重新搜索“{name}”…";
    }

    [RelayCommand]
    private void SearchHistory(string keyword)
    {
        Keyword = keyword;
        SearchCommand.Execute(null);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        History.Clear();
        AppSettings.Current.SearchHistory.Clear();
        AppSettings.Current.Save();
    }

    private void AddHistory(string keyword)
    {
        History.Remove(keyword);
        History.Insert(0, keyword);
        while (History.Count > 20) History.RemoveAt(History.Count - 1);
        AppSettings.Current.SearchHistory = History.ToList();
        AppSettings.Current.Save();
    }
}
