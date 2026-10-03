using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using TingShu.Sdk;

namespace tingshu.Core;

/// <summary>
/// 书架上的一本书（收藏 / 播放记录 / 播放进度 / 单书设置）
/// </summary>
public partial class BookRecord : ObservableObject
{
    public string SourceId { get; set; } = "";
    public string BookUrl { get; set; } = "";

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _coverUrl = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _intro = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private DateTime? _lastPlayed;
    public DateTime AddedAt { get; set; } = DateTime.Now;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProgressText))] private int _episodeIndex;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProgressText))] private string? _episodeTitle;
    public string? EpisodeUrl { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProgressText))] private long _positionMs;
    public long DurationMs { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProgressText))] private int _episodeCount;

    /// <summary>单书播放速度，0 表示使用默认值</summary>
    public double Speed { get; set; }
    /// <summary>跳过片头（秒）</summary>
    [ObservableProperty] private int _skipIntro;
    /// <summary>跳过片尾（秒）</summary>
    [ObservableProperty] private int _skipOutro;
    [ObservableProperty] private bool _reversed;

    [JsonIgnore]
    public string Key => MakeKey(SourceId, BookUrl);

    [JsonIgnore]
    public string SourceName => PluginManager.Instance.Find(SourceId)?.Name ?? "未知来源";

    [JsonIgnore]
    public string ProgressText
    {
        get
        {
            if (string.IsNullOrEmpty(EpisodeTitle)) return EpisodeCount > 0 ? $"共 {EpisodeCount} 集 · 未收听" : "未收听";
            var pos = TimeSpan.FromMilliseconds(PositionMs);
            var t = pos.TotalHours >= 1 ? pos.ToString(@"h\:mm\:ss") : pos.ToString(@"mm\:ss");
            return EpisodeCount > 0 ? $"第 {EpisodeIndex + 1}/{EpisodeCount} 集 · {t}" : $"{EpisodeTitle} · {t}";
        }
    }

    public static string MakeKey(string sourceId, string bookUrl) => sourceId + "|" + bookUrl;

    public Book ToBook() => new(CoverUrl, BookUrl, Title, Author, Artist) { Intro = Intro, Status = Status, SourceId = SourceId };

    public void UpdateFrom(Book book)
    {
        if (!string.IsNullOrEmpty(book.Title)) Title = book.Title;
        if (!string.IsNullOrEmpty(book.CoverUrl)) CoverUrl = book.CoverUrl;
        if (!string.IsNullOrEmpty(book.Author)) Author = book.Author;
        if (!string.IsNullOrEmpty(book.Artist)) Artist = book.Artist;
        var intro = TextUtil.CleanHtml(book.Intro);
        if (!string.IsNullOrEmpty(intro)) Intro = intro;
        if (!string.IsNullOrEmpty(book.Status)) Status = book.Status;
    }
}

public class LibraryData
{
    public List<BookRecord> Books { get; set; } = new();
    public string? LastPlayingKey { get; set; }
}

/// <summary>
/// 书架存储
/// </summary>
public sealed class LibraryStore
{
    public static LibraryStore Instance { get; } = new();

    private readonly LibraryData _data;
    private readonly Dictionary<string, BookRecord> _index;
    private readonly Debouncer _saver = new(TimeSpan.FromSeconds(1));

    public ObservableCollection<BookRecord> Favorites { get; } = new();
    public ObservableCollection<BookRecord> Recent { get; } = new();

    private LibraryStore()
    {
        _data = JsonStore.Load<LibraryData>(AppPaths.LibraryFile);
        _index = new Dictionary<string, BookRecord>();
        foreach (var b in _data.Books)
        {
            b.Intro = TextUtil.CleanHtml(b.Intro);
            _index[b.Key] = b;
        }
        RefreshCollections();
    }

    public BookRecord? LastPlaying => _data.LastPlayingKey != null && _index.TryGetValue(_data.LastPlayingKey, out var b) ? b : null;

    public BookRecord? Find(string sourceId, string bookUrl) => _index.GetValueOrDefault(BookRecord.MakeKey(sourceId, bookUrl));

    /// <summary>获取（不存在则创建一个临时记录，只有收藏或播放后才会保存）</summary>
    public BookRecord GetOrCreate(Book book)
    {
        if (_index.TryGetValue(BookRecord.MakeKey(book.SourceId, book.BookUrl), out var record))
        {
            record.UpdateFrom(book);
            return record;
        }
        record = new BookRecord { SourceId = book.SourceId, BookUrl = book.BookUrl };
        record.UpdateFrom(book);
        return record;
    }

    private void Attach(BookRecord record)
    {
        if (_index.ContainsKey(record.Key)) return;
        _index[record.Key] = record;
        _data.Books.Add(record);
    }

    public void SetFavorite(BookRecord record, bool favorite)
    {
        record.IsFavorite = favorite;
        if (favorite) Attach(record);
        Cleanup(record);
        Changed();
    }

    public void MarkPlayed(BookRecord record)
    {
        record.LastPlayed = DateTime.Now;
        Attach(record);
        _data.LastPlayingKey = record.Key;
        Changed();
    }

    public void UpdateProgress(BookRecord record, int index, Episode episode, long positionMs, long durationMs)
    {
        record.EpisodeIndex = index;
        record.EpisodeTitle = episode.Title;
        record.EpisodeUrl = episode.Url;
        record.PositionMs = positionMs;
        record.DurationMs = durationMs;
        Save();
    }

    public void RemoveFromRecent(BookRecord record)
    {
        record.LastPlayed = null;
        Cleanup(record);
        Changed();
    }

    private void Cleanup(BookRecord record)
    {
        if (!record.IsFavorite && record.LastPlayed == null)
        {
            _index.Remove(record.Key);
            _data.Books.Remove(record);
        }
    }

    private void Changed()
    {
        RefreshCollections();
        Save();
    }

    private void RefreshCollections()
    {
        Sync(Favorites, _data.Books.Where(b => b.IsFavorite).OrderByDescending(b => b.LastPlayed ?? b.AddedAt));
        Sync(Recent, _data.Books.Where(b => b.LastPlayed != null).OrderByDescending(b => b.LastPlayed).Take(100));
    }

    private static void Sync(ObservableCollection<BookRecord> target, IEnumerable<BookRecord> items)
    {
        var list = items.ToList();
        for (var i = target.Count - 1; i >= 0; i--)
            if (!list.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < list.Count; i++)
        {
            var idx = target.IndexOf(list[i]);
            if (idx == i) continue;
            if (idx >= 0) target.Move(idx, i);
            else target.Insert(i, list[i]);
        }
    }

    public void Save() => _saver.Run(() => JsonStore.Save(AppPaths.LibraryFile, _data));
    public void SaveNow() => JsonStore.Save(AppPaths.LibraryFile, _data);

    // ---------- 章节缓存：加快继续播放 ----------

    private static string EpisodeCachePath(string sourceId, string bookUrl)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sourceId + "|" + bookUrl)));
        return Path.Combine(AppPaths.Ensure(Path.Combine(AppPaths.CacheDir, "episodes")), hash + ".json");
    }

    public static List<Episode>? LoadEpisodes(string sourceId, string bookUrl)
    {
        var path = EpisodeCachePath(sourceId, bookUrl);
        return File.Exists(path) ? JsonStore.Load<List<Episode>>(path) : null;
    }

    public static void SaveEpisodes(string sourceId, string bookUrl, IList<Episode> episodes)
    {
        if (episodes.Count == 0) return;
        var list = episodes.ToList();
        Task.Run(() => JsonStore.Save(EpisodeCachePath(sourceId, bookUrl), list));
    }
}
