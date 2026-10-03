using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TingShu.Sdk;
using tingshu.Core;

namespace tingshu.ViewModels;

/// <summary>列表中展示的书籍</summary>
public sealed class BookItem
{
    public BookItem(Book book)
    {
        book.Intro = TextUtil.CleanHtml(book.Intro).Replace('\n', ' ');
        Book = book;
        SourceName = PluginManager.Instance.Find(book.SourceId)?.Name ?? "";
    }

    public Book Book { get; }
    public string SourceName { get; }
    public string Meta => string.Join(" · ", new[] { Book.Author, Book.Artist }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed partial class MainViewModel : ObservableObject
{
    public static MainViewModel Instance { get; } = new();

    private readonly Stack<(string Key, object Page)> _history = new();
    private CancellationTokenSource? _toastCts;

    public DiscoverViewModel Discover { get; } = new();
    public SearchViewModel Search { get; } = new();
    public LibraryViewModel Library { get; } = new();
    public PluginsViewModel Plugins { get; } = new();
    public SettingsViewModel Settings { get; } = new();
    public PlayerService Player => PlayerService.Instance;

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _navKey = "library";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanGoBack))] private int _historyCount;
    [ObservableProperty] private string? _toastText;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private bool _isCompact;
    [ObservableProperty] private bool _isNarrow;

    public bool CanGoBack => HistoryCount > 0;

    private MainViewModel()
    {
        PlayerService.Instance.Message += ShowToast;
        SourceHost.ToastRequested += msg => Application.Current.Dispatcher.InvokeAsync(() => ShowToast(msg));
    }

    public void Start()
    {
        NavKey = LibraryStore.Instance.Favorites.Count > 0 || LibraryStore.Instance.Recent.Count > 0 ? "library" : "discover";
        Navigate(NavKey);
    }

    partial void OnNavKeyChanged(string value) => Navigate(value);

    private object PageFor(string key) => key switch
    {
        "discover" => Discover,
        "search" => Search,
        "plugins" => Plugins,
        "settings" => Settings,
        _ => Library,
    };

    public void Navigate(string key)
    {
        _history.Clear();
        HistoryCount = 0;
        CurrentPage = PageFor(key);
        if (CurrentPage is IPageActivated a) a.OnActivated();
    }

    private void Push(object page)
    {
        if (CurrentPage != null) _history.Push((NavKey, CurrentPage));
        HistoryCount = _history.Count;
        CurrentPage = page;
    }

    [RelayCommand]
    public void GoBack()
    {
        if (_history.Count == 0) return;
        var (_, page) = _history.Pop();
        HistoryCount = _history.Count;
        if (CurrentPage is IDisposable d) d.Dispose();
        CurrentPage = page;
        if (page is IPageActivated a) a.OnActivated();
    }

    [RelayCommand]
    public void OpenBook(object? item)
    {
        var book = item switch
        {
            BookItem bi => bi.Book,
            Book b => b,
            BookRecord r => r.ToBook(),
            _ => null,
        };
        if (book == null) return;
        Push(new BookDetailViewModel(book));
    }

    /// <summary>打开正在播放的书</summary>
    [RelayCommand]
    public void OpenPlaying()
    {
        if (Player.Book == null) return;
        if (CurrentPage is BookDetailViewModel d && d.Record == Player.Book) return;
        Push(new BookDetailViewModel(Player.Book.ToBook()));
    }

    [RelayCommand]
    public void SearchFor(string? keyword)
    {
        NavKey = "search";
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            Search.Keyword = keyword;
            Search.SearchCommand.Execute(null);
        }
    }

    public async void ShowToast(string message)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        ToastText = message;
        IsToastVisible = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(message.Length / 8.0, 2.5, 6)), cts.Token);
            IsToastVisible = false;
        }
        catch (TaskCanceledException) { }
    }
}

/// <summary>页面重新显示时刷新</summary>
public interface IPageActivated
{
    void OnActivated();
}
