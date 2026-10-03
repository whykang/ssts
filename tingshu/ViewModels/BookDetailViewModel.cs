using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TingShu.Sdk;
using tingshu.Core;

namespace tingshu.ViewModels;

public sealed partial class BookDetailViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private List<Episode> _all = new();

    public BookDetailViewModel(Book book)
    {
        Record = LibraryStore.Instance.GetOrCreate(book);
        Entry = PluginManager.Instance.Find(book.SourceId);
        SourceHost.ProgressReported += OnProgress;
        _ = LoadAsync();
    }

    public BookRecord Record { get; }
    public SourceEntry? Entry { get; }
    public PlayerService Player => PlayerService.Instance;
    public string SourceName => Entry?.Name ?? "插件不存在";
    public string Meta => string.Join("  ·  ", new[]
    {
        string.IsNullOrWhiteSpace(Record.Author) ? null : "作者 " + Record.Author,
        string.IsNullOrWhiteSpace(Record.Artist) ? null : "演播 " + Record.Artist,
    }.Where(s => s != null));

    public ObservableCollection<Episode> Episodes { get; } = new();

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowErrorOverlay))] private string? _error;
    [ObservableProperty] private string? _loadingInfo;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowErrorOverlay))] private int _episodeCount;

    /// <summary>没有任何章节时才用大块区域显示错误，否则在标题旁显示</summary>
    public bool ShowErrorOverlay => !string.IsNullOrEmpty(Error) && EpisodeCount == 0;

    public string PlayButtonText => !string.IsNullOrEmpty(Record.EpisodeUrl) ? "继续播放" : "开始播放";
    public string? ResumeHint => !string.IsNullOrEmpty(Record.EpisodeTitle) ? $"上次听到：{Record.EpisodeTitle}" : null;

    private void OnProgress(string sourceId, string? info)
    {
        if (sourceId != Record.SourceId) return;
        Application.Current.Dispatcher.InvokeAsync(() => LoadingInfo = info == null ? null : $"正在加载章节 {info}");
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Entry == null)
        {
            IsLoading = false;
            Error = "这本书的来源插件已被删除或禁用";
            // 有缓存时仍可播放
            var cached = LibraryStore.LoadEpisodes(Record.SourceId, Record.BookUrl);
            if (cached != null) SetEpisodes(cached, false);
            return;
        }

        IsLoading = true;
        Error = null;
        var cachedEpisodes = LibraryStore.LoadEpisodes(Record.SourceId, Record.BookUrl);
        if (cachedEpisodes is { Count: > 0 }) SetEpisodes(cachedEpisodes, false);

        try
        {
            var source = Entry.Source;
            // 章节分很多页的书，第一次打开时先显示第一页，能马上开始播放，其余的在后面继续加载
            if (source.IsMultipleEpisodePages && Episodes.Count == 0)
            {
                var first = await Task.Run(() => source.GetBookDetailAsync(Record.BookUrl, true, false, _cts.Token), _cts.Token);
                if (first.Episodes.Count > 0)
                {
                    var preview = first.Episodes.ToList();
                    if (Record.Reversed) preview.Reverse();
                    SetEpisodes(preview, false);
                    LoadingInfo = "正在加载其余章节…";
                }
            }
            BookDetail? detail = null;
            // 已有章节缓存、源支持增量更新时，只取新增的章节（缓存按显示顺序保存，倒序时先翻回网站顺序）
            if (source is IIncrementalEpisodes incremental && cachedEpisodes is { Count: > 0 })
            {
                var known = cachedEpisodes.ToList();
                if (Record.Reversed) known.Reverse();
                detail = await Task.Run(() => incremental.UpdateEpisodesAsync(Record.BookUrl, known, _cts.Token), _cts.Token);
            }
            detail ??= await Task.Run(() => source.GetBookDetailAsync(Record.BookUrl, true, true, _cts.Token), _cts.Token);
            var intro = TextUtil.CleanHtml(detail.Intro);
            if (intro.Length > 0) Record.Intro = intro;
            if (!string.IsNullOrWhiteSpace(detail.Author)) Record.Author = detail.Author;
            if (!string.IsNullOrWhiteSpace(detail.Artist)) Record.Artist = detail.Artist;
            if (!string.IsNullOrWhiteSpace(detail.CoverUrl)) Record.CoverUrl = detail.CoverUrl;
            if (!string.IsNullOrWhiteSpace(detail.Title)) Record.Title = detail.Title;
            if (!string.IsNullOrWhiteSpace(detail.Status)) Record.Status = detail.Status;
            OnPropertyChanged(nameof(Meta));

            var list = detail.Episodes.ToList();
            if (Record.Reversed) list.Reverse();
            SetEpisodes(list, true);
            if (list.Count == 0) Error = "没有获取到章节";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error($"加载详情失败 {Record.BookUrl}", ex);
            Error = (Episodes.Count > 0 ? "刷新章节失败（显示的是缓存）：" : "加载失败：") + ex.Message;
        }
        finally
        {
            IsLoading = false;
            LoadingInfo = null;
        }
    }

    private void SetEpisodes(List<Episode> list, bool save)
    {
        _all = list;
        EpisodeCount = list.Count;
        Record.EpisodeCount = list.Count;
        if (save && (Record.IsFavorite || Record.LastPlayed != null))
            LibraryStore.SaveEpisodes(Record.SourceId, Record.BookUrl, list);
        // 正在播放这本书时，同步最新章节列表
        if (save && Player.Book == Record && Player.CurrentEpisode is { } cur)
        {
            var idx = list.FindIndex(e => e.Url == cur.Url);
            if (idx >= 0)
            {
                Player.Episodes = list;
                Player.EpisodeIndex = idx;
            }
        }
        ApplyFilter();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Episodes.Clear();
        var f = Filter.Trim();
        foreach (var e in string.IsNullOrEmpty(f) ? _all : _all.Where(e => e.Title.Contains(f, StringComparison.OrdinalIgnoreCase)))
            Episodes.Add(e);
    }

    [RelayCommand]
    private async Task PlayEpisode(Episode? episode)
    {
        if (episode == null) return;
        var index = _all.IndexOf(episode);
        if (index < 0) return;
        long start = episode.Url == Record.EpisodeUrl ? Record.PositionMs : 0;
        await Player.PlayAsync(Record, _all, index, start);
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(ResumeHint));
    }

    [RelayCommand]
    private async Task PlayOrResume()
    {
        if (Player.Book == Record && Player.CurrentEpisode != null)
        {
            Player.TogglePlay();
            return;
        }
        if (_all.Count == 0) return;
        var index = !string.IsNullOrEmpty(Record.EpisodeUrl) ? _all.FindIndex(e => e.Url == Record.EpisodeUrl) : -1;
        if (index < 0 && !string.IsNullOrEmpty(Record.EpisodeUrl)) index = Math.Clamp(Record.EpisodeIndex, 0, _all.Count - 1);
        var start = index >= 0 ? Record.PositionMs : 0;
        await Player.PlayAsync(Record, _all, Math.Max(index, 0), start);
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(ResumeHint));
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        LibraryStore.Instance.SetFavorite(Record, !Record.IsFavorite);
        if (Record.IsFavorite) LibraryStore.SaveEpisodes(Record.SourceId, Record.BookUrl, _all);
        MainViewModel.Instance.ShowToast(Record.IsFavorite ? "已加入书架" : "已移出书架");
    }

    [RelayCommand]
    private void ToggleReverse()
    {
        Record.Reversed = !Record.Reversed;
        _all.Reverse();
        LibraryStore.Instance.Save();
        if (Player.Book == Record && Player.CurrentEpisode is { } cur)
        {
            Player.Episodes = _all.ToList();
            Player.EpisodeIndex = Player.Episodes.IndexOf(cur);
        }
        ApplyFilter();
    }

    [RelayCommand]
    private void SearchOtherSources()
    {
        var title = Record.Title;
        var idx = title.IndexOf(']');
        if (idx >= 0 && idx < title.Length - 1) title = title[(idx + 1)..];
        MainViewModel.Instance.SearchFor(title.Split('|', '｜', '（', '(')[0].Trim());
    }

    public void SaveBookSettings() => LibraryStore.Instance.Save();

    public void Dispose()
    {
        SourceHost.ProgressReported -= OnProgress;
        _cts.Cancel();
    }
}
