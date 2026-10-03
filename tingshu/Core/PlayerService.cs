using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TingShu.Sdk;

namespace tingshu.Core;

public enum SleepMode { Off, Minutes, EndOfEpisode }

/// <summary>音频地址可以解析，但文件本身已失效</summary>
public sealed class MediaUnavailableException(string message) : Exception(message);

/// <summary>
/// 播放器：解析音频地址 → 本地代理 → 系统媒体播放（Media Foundation，支持 mp3 / m4a / aac / flac / wav / wma）
/// 必须在 UI 线程创建与调用。
/// </summary>
public sealed partial class PlayerService : ObservableObject
{
    public static PlayerService Instance { get; } = new();

    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _loadCts;
    private long _pendingSeekMs;
    private DateTime _lastSave = DateTime.MinValue;
    private DateTime? _sleepAt;
    private int _failCount;
    private bool _isOpened;

    public event Action<string>? Message;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasBook))] private BookRecord? _book;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurrentEpisode))] private IList<Episode> _episodes = Array.Empty<Episode>();
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EpisodeTitle), nameof(CurrentEpisode))] private int _episodeIndex = -1;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PositionText))] private double _position;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DurationText), nameof(SeekMaximum))] private double _duration;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SpeedText))] private double _speed = 1.0;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private SleepMode _sleepMode;
    [ObservableProperty] private string _sleepText = "";

    /// <summary>用户正在拖动进度条时不要更新位置</summary>
    public bool IsSeeking { get; set; }

    public bool HasBook => Book != null;
    public Episode? CurrentEpisode => EpisodeIndex >= 0 && EpisodeIndex < Episodes.Count ? Episodes[EpisodeIndex] : null;
    public string EpisodeTitle => CurrentEpisode?.Title ?? Book?.EpisodeTitle ?? "";
    public string PositionText => FormatTime(Position);
    public string DurationText => FormatTime(Duration);
    /// <summary>时长未知时给进度条一个非零最大值，避免显示为满格</summary>
    public double SeekMaximum => Duration > 0 ? Duration : 1;
    public string SpeedText => Math.Abs(Speed - 1) < 0.001 ? "倍速" : $"{Speed:0.##}x";

    public static readonly double[] SpeedOptions = { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 };

    private PlayerService()
    {
        _volume = AppSettings.Current.Volume;
        _player.Volume = _volume;
        _speed = AppSettings.Current.DefaultSpeed;
        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += (_, _) => OnMediaEnded();
        _player.MediaFailed += (_, e) => OnMediaFailed(e.ErrorException);
        _player.BufferingStarted += (_, _) => IsLoading = true;
        _player.BufferingEnded += (_, _) => IsLoading = false;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    /// <summary>载入一本书但不播放（用于启动时恢复）</summary>
    public void Prepare(BookRecord book)
    {
        Book = book;
        Episodes = LibraryStore.LoadEpisodes(book.SourceId, book.BookUrl) ?? new List<Episode>();
        EpisodeIndex = Episodes.Count > 0 ? Math.Clamp(FindIndex(book), 0, Episodes.Count - 1) : -1;
        Position = book.PositionMs / 1000.0;
        Duration = book.DurationMs / 1000.0;
        _pendingSeekMs = book.PositionMs;
        _isOpened = false;
        OnPropertyChanged(nameof(EpisodeTitle));
    }

    private static int FindIndex(BookRecord book)
    {
        var episodes = LibraryStore.LoadEpisodes(book.SourceId, book.BookUrl);
        if (episodes == null) return book.EpisodeIndex;
        var byUrl = episodes.FindIndex(e => e.Url == book.EpisodeUrl);
        return byUrl >= 0 ? byUrl : book.EpisodeIndex;
    }

    /// <summary>播放指定书籍的某一集</summary>
    public async Task PlayAsync(BookRecord book, IList<Episode> episodes, int index, long startMs = 0)
    {
        if (episodes.Count == 0)
        {
            Message?.Invoke("章节列表为空");
            return;
        }
        index = Math.Clamp(index, 0, episodes.Count - 1);
        SaveProgress();

        if (Book != book)
        {
            Book = book;
            if (book.Speed > 0) Speed = book.Speed;
        }
        Episodes = episodes;
        EpisodeIndex = index;
        OnPropertyChanged(nameof(EpisodeTitle));
        LibraryStore.Instance.MarkPlayed(book);
        book.EpisodeCount = episodes.Count;
        LibraryStore.SaveEpisodes(book.SourceId, book.BookUrl, episodes);

        var episode = episodes[index];
        _pendingSeekMs = Math.Max(startMs, book.SkipIntro * 1000L);
        await LoadAndPlayAsync(episode);
    }

    /// <summary>继续播放书架上的书（使用缓存的章节，没有则在线获取）</summary>
    public async Task ResumeAsync(BookRecord book)
    {
        var episodes = LibraryStore.LoadEpisodes(book.SourceId, book.BookUrl);
        if (episodes == null || episodes.Count == 0)
        {
            var entry = PluginManager.Instance.Find(book.SourceId);
            if (entry == null)
            {
                Message?.Invoke("找不到这本书的来源插件");
                return;
            }
            IsLoading = true;
            try
            {
                var detail = await Task.Run(() => entry.Source.GetBookDetailAsync(book.BookUrl, true, true, CancellationToken.None));
                episodes = detail.Episodes.ToList();
                if (book.Reversed) episodes.Reverse();
            }
            catch (Exception ex)
            {
                IsLoading = false;
                Message?.Invoke("获取章节失败：" + ex.Message);
                return;
            }
        }
        var index = episodes.FindIndex(e => e.Url == book.EpisodeUrl);
        if (index < 0) index = Math.Clamp(book.EpisodeIndex, 0, episodes.Count - 1);
        var start = index == book.EpisodeIndex || episodes[index].Url == book.EpisodeUrl ? book.PositionMs : 0;
        await PlayAsync(book, episodes, index, start);
    }

    private async Task LoadAndPlayAsync(Episode episode)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var entry = PluginManager.Instance.Find(Book?.SourceId);
        if (entry == null)
        {
            Message?.Invoke("找不到这本书的来源插件，可能已被删除");
            return;
        }

        _player.Stop();
        _isOpened = false;
        IsPlaying = false;
        IsLoading = true;
        Error = null;
        Position = _pendingSeekMs / 1000.0;
        Duration = 0;

        if (!episode.IsFree) Message?.Invoke("这是付费章节，可能需要在插件页面登录后才能播放");

        try
        {
            var (url, headers) = await AudioResolver.ResolveAsync(entry, episode, cts.Token);
            if (cts.IsCancellationRequested) return;
            await EnsureAudioAvailable(url, headers, cts.Token);
            if (cts.IsCancellationRequested) return;
            var uri = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? AudioProxy.Instance.Register(url, headers)
                : new Uri(url);
            _player.Open(uri);
            _player.Play();
            IsPlaying = true;
            NotifySource("playing");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;
            IsLoading = false;
            Error = ex.Message;
            Message?.Invoke($"播放失败：{ex.Message}");
            Logger.Error($"解析音频失败 {episode.Url}", ex);
            NotifySource("error");
        }
    }

    /// <summary>
    /// 播放前探测音频地址：网站上的音频链接经常失效，提前给出明确提示，
    /// 而不是让系统播放器报含糊的“找不到媒体文件”。探测本身出错（超时等）时不拦截。
    /// </summary>
    private static async Task EnsureAudioAvailable(string url, IDictionary<string, string>? headers, CancellationToken ct)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        int status;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", HttpService.UserAgent(false));
            request.Headers.TryAddWithoutValidation("Range", "bytes=0-1");
            if (headers != null)
                foreach (var (k, v) in headers) request.Headers.TryAddWithoutValidation(k, v);
            using var response = await HttpService.Client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            status = (int)response.StatusCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return;
        }
        if (status is 404 or 410)
            throw new MediaUnavailableException($"音频文件已失效（服务器返回 {status}），网站上这一集已经无法播放");
        if (status is 401 or 403)
            throw new MediaUnavailableException($"音频服务器拒绝访问（{status}），可能需要登录或该内容已下架");
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        _isOpened = true;
        _failCount = 0;
        IsLoading = false;
        Duration = _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan.TotalSeconds : 0;
        if (_pendingSeekMs > 0 && (Duration <= 0 || _pendingSeekMs / 1000.0 < Duration - 3))
            _player.Position = TimeSpan.FromMilliseconds(_pendingSeekMs);
        _pendingSeekMs = 0;
        _player.SpeedRatio = Speed;
        _player.Volume = IsMuted ? 0 : Volume;
    }

    private void OnMediaEnded()
    {
        SaveProgress(force: true);
        if (SleepMode == SleepMode.EndOfEpisode)
        {
            CancelSleep();
            Pause();
            Message?.Invoke("定时结束，已暂停");
            return;
        }
        if (AppSettings.Current.AutoPlayNext && EpisodeIndex < Episodes.Count - 1) _ = Next();
        else
        {
            IsPlaying = false;
            NotifySource("stopped");
        }
    }

    private void OnMediaFailed(Exception ex)
    {
        Logger.Error("播放出错", ex);
        IsLoading = false;
        IsPlaying = false;
        // 地址可能过期，自动重新解析一次
        if (_failCount++ < 1 && CurrentEpisode is { } ep)
        {
            _pendingSeekMs = (long)(Position * 1000);
            _ = LoadAndPlayAsync(ep);
            return;
        }
        Error = ex.Message;
        Message?.Invoke("无法播放该音频：" + ex.Message);
        NotifySource("error");
    }

    private void Tick()
    {
        if (_isOpened && IsPlaying && !IsSeeking)
        {
            Position = _player.Position.TotalSeconds;
            if (Duration <= 0 && _player.NaturalDuration.HasTimeSpan) Duration = _player.NaturalDuration.TimeSpan.TotalSeconds;
            // 跳过片尾
            if (Book is { SkipOutro: > 0 } b && Duration > b.SkipOutro + 10 && Position >= Duration - b.SkipOutro)
            {
                _player.Stop();
                OnMediaEnded();
            }
            if ((DateTime.Now - _lastSave).TotalSeconds >= 5) SaveProgress();
        }

        if (SleepMode == SleepMode.Minutes && _sleepAt is { } at)
        {
            var left = at - DateTime.Now;
            if (left <= TimeSpan.Zero)
            {
                CancelSleep();
                Pause();
                Message?.Invoke("定时结束，已暂停");
            }
            else SleepText = left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        }
    }

    private void SaveProgress(bool force = false)
    {
        if (Book == null || CurrentEpisode == null) return;
        if (!_isOpened && !force) return;
        _lastSave = DateTime.Now;
        LibraryStore.Instance.UpdateProgress(Book, EpisodeIndex, CurrentEpisode, (long)(Position * 1000), (long)(Duration * 1000));
    }

    private void NotifySource(string state)
    {
        try { (PluginManager.Instance.Find(Book?.SourceId)?.Source as IPlaybackStateListener)?.OnPlaybackStateChanged(state); }
        catch (Exception ex) { Logger.Error("插件回调失败", ex); }
    }

    // ---------------- 控制 ----------------

    [RelayCommand]
    public void TogglePlay()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        if (Book == null) return;
        if (!_isOpened)
        {
            if (CurrentEpisode is { } ep) _ = LoadAndPlayAsync(ep);
            else _ = ResumeAsync(Book);
            return;
        }
        _player.Play();
        _player.SpeedRatio = Speed;
        IsPlaying = true;
        NotifySource("playing");
    }

    public void Pause()
    {
        if (_isOpened) _player.Pause();
        IsPlaying = false;
        SaveProgress(force: true);
        NotifySource("paused");
    }

    [RelayCommand]
    public async Task Next()
    {
        if (Book == null || EpisodeIndex >= Episodes.Count - 1)
        {
            Message?.Invoke("已经是最后一集了");
            return;
        }
        await PlayAsync(Book, Episodes, EpisodeIndex + 1);
    }

    [RelayCommand]
    public async Task Previous()
    {
        if (Book == null || EpisodeIndex <= 0)
        {
            Message?.Invoke("已经是第一集了");
            return;
        }
        await PlayAsync(Book, Episodes, EpisodeIndex - 1);
    }

    [RelayCommand]
    public void Forward() => SeekTo(Position + AppSettings.Current.SeekStepSeconds);

    [RelayCommand]
    public void Rewind() => SeekTo(Position - AppSettings.Current.SeekStepSeconds);

    public void SeekTo(double seconds)
    {
        seconds = Math.Max(0, Duration > 0 ? Math.Min(seconds, Duration - 1) : seconds);
        Position = seconds;
        if (_isOpened) _player.Position = TimeSpan.FromSeconds(seconds);
        else _pendingSeekMs = (long)(seconds * 1000);
    }

    [RelayCommand]
    public void SetSpeed(double speed)
    {
        Speed = speed;
        if (Book != null)
        {
            Book.Speed = speed;
            LibraryStore.Instance.Save();
        }
    }

    partial void OnSpeedChanged(double value)
    {
        if (_isOpened) _player.SpeedRatio = value;
    }

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = IsMuted ? 0 : value;
        AppSettings.Current.Volume = value;
        AppSettings.Current.Save();
    }

    partial void OnIsMutedChanged(bool value) => _player.Volume = value ? 0 : Volume;

    [RelayCommand]
    public void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    public void SetSleep(string arg)
    {
        if (arg == "off") { CancelSleep(); Message?.Invoke("已关闭定时"); return; }
        if (arg == "end")
        {
            SleepMode = SleepMode.EndOfEpisode;
            _sleepAt = null;
            SleepText = "本集结束";
            Message?.Invoke("将在本集播放完毕后暂停");
            return;
        }
        if (int.TryParse(arg, out var minutes))
        {
            SleepMode = SleepMode.Minutes;
            _sleepAt = DateTime.Now.AddMinutes(minutes);
            SleepText = $"{minutes}:00";
            Message?.Invoke($"将在 {minutes} 分钟后暂停");
        }
    }

    private void CancelSleep()
    {
        SleepMode = SleepMode.Off;
        _sleepAt = null;
        SleepText = "";
    }

    public void Shutdown()
    {
        SaveProgress(force: true);
        _player.Close();
    }

    public static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }
}
