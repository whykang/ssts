using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using tingshu.Controls;
using tingshu.Core;
using tingshu.Themes;
using tingshu.ViewModels;

namespace tingshu;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = MainViewModel.Instance;
    private ThumbButtonInfo? _thumbPlay;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowBounds();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateMaximizedState();
        SizeChanged += (_, _) => UpdateResponsive();
        PreviewKeyDown += OnPreviewKeyDown;
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) _vm.GoBack();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizedState();
        UpdateResponsive();
        SetupTaskbarButtons();
        _vm.Start();

        // 恢复上次播放（暂停状态）
        if (AppSettings.Current.ResumeOnStartup && LibraryStore.Instance.LastPlaying is { } last)
            PlayerService.Instance.Prepare(last);

        PlayerService.Instance.PropertyChanged += OnPlayerChanged;
        AudioProxy.Instance.Start();
    }

    private void RestoreWindowBounds()
    {
        var s = AppSettings.Current;
        var area = SystemParameters.WorkArea;
        if (s.WindowWidth > 0 && s.WindowHeight > 0)
        {
            Width = Math.Min(s.WindowWidth, area.Width);
            Height = Math.Min(s.WindowHeight, area.Height);
        }
        else
        {
            // 首次启动：按屏幕大小自适应（小屏笔记本也能完整显示）
            Width = Math.Min(1280, area.Width * 0.86);
            Height = Math.Min(820, area.Height * 0.88);
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var s = AppSettings.Current;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        base.OnClosing(e);
    }

    // ---------------- 自适应布局 ----------------

    private void UpdateResponsive()
    {
        var w = ActualWidth;
        var compact = w < 1040;
        var narrow = w < 860;
        _vm.IsCompact = compact;
        _vm.IsNarrow = narrow;
        Sidebar.Width = compact ? 68 : 216;
        Ui.SetShowLabel(Sidebar, !compact);
        LogoText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        GlobalSearch.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        GlobalSearch.Width = w < 1200 ? 200 : 260;
        VolumeSlider.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        RightColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    private void UpdateMaximizedState()
    {
        // WindowChrome 最大化时窗口会超出屏幕边缘，需要补偿
        var frame = SystemParameters.WindowNonClientFrameThickness.Left;
        ShellRoot.Margin = WindowState == WindowState.Maximized ? new Thickness(frame) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------- 搜索 ----------------

    private void GlobalSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var text = GlobalSearch.Text.Trim();
        if (text.Length == 0) return;
        _vm.SearchFor(text);
        GlobalSearch.Text = "";
    }

    // ---------------- 进度条拖动 ----------------

    private void Seek_MouseDown(object sender, MouseButtonEventArgs e) => PlayerService.Instance.IsSeeking = true;

    private void Seek_MouseUp(object sender, MouseButtonEventArgs e) => CommitSeek();

    private void Seek_LostCapture(object sender, MouseEventArgs e)
    {
        if (PlayerService.Instance.IsSeeking) CommitSeek();
    }

    private void CommitSeek()
    {
        var player = PlayerService.Instance;
        player.IsSeeking = false;
        player.SeekTo(SeekSlider.Value);
    }

    // ---------------- 快捷键 ----------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var player = PlayerService.Instance;
        var inText = Keyboard.FocusedElement is TextBox;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        switch (e.Key)
        {
            case Key.MediaPlayPause:
            case Key.Space when !inText && Keyboard.FocusedElement is not ButtonBase:
                player.TogglePlay();
                e.Handled = true;
                break;
            case Key.MediaNextTrack:
            case Key.Right when ctrl && !inText:
                _ = player.Next();
                e.Handled = true;
                break;
            case Key.MediaPreviousTrack:
            case Key.Left when ctrl && !inText:
                _ = player.Previous();
                e.Handled = true;
                break;
            case Key.Right when !inText && Keyboard.FocusedElement is not Slider and not ListBoxItem:
                player.Forward();
                e.Handled = true;
                break;
            case Key.Left when !inText && Keyboard.FocusedElement is not Slider and not ListBoxItem:
                player.Rewind();
                e.Handled = true;
                break;
            case Key.Left when Keyboard.Modifiers == ModifierKeys.Alt:
            case Key.BrowserBack:
                _vm.GoBack();
                e.Handled = true;
                break;
            case Key.F when ctrl:
                if (GlobalSearch.IsVisible) GlobalSearch.Focus();
                else _vm.NavKey = "search";
                e.Handled = true;
                break;
        }
    }

    // ---------------- 任务栏缩略图按钮 ----------------

    private static readonly ImageSource PlayImage = GlyphImage.Create("", Brushes.White);
    private static readonly ImageSource PauseImage = GlyphImage.Create("", Brushes.White);

    private void SetupTaskbarButtons()
    {
        var prev = new ThumbButtonInfo { Description = "上一集", ImageSource = GlyphImage.Create("", Brushes.White) };
        prev.Click += (_, _) => _ = PlayerService.Instance.Previous();
        _thumbPlay = new ThumbButtonInfo { Description = "播放", ImageSource = PlayImage };
        _thumbPlay.Click += (_, _) => PlayerService.Instance.TogglePlay();
        var next = new ThumbButtonInfo { Description = "下一集", ImageSource = GlyphImage.Create("", Brushes.White) };
        next.Click += (_, _) => _ = PlayerService.Instance.Next();
        Taskbar.ThumbButtonInfos = new ThumbButtonInfoCollection { prev, _thumbPlay, next };
        UpdateTaskbar();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.IsPlaying) or nameof(PlayerService.Position) or nameof(PlayerService.Book) or nameof(PlayerService.EpisodeIndex))
            UpdateTaskbar();
    }

    private void UpdateTaskbar()
    {
        var p = PlayerService.Instance;
        if (_thumbPlay != null)
        {
            _thumbPlay.ImageSource = p.IsPlaying ? PauseImage : PlayImage;
            _thumbPlay.Description = p.IsPlaying ? "暂停" : "播放";
        }
        Title = p.Book != null && p.IsPlaying ? $"{p.EpisodeTitle} - {p.Book.Title} - 随身听书" : "随身听书";
        if (p.Duration > 0 && p.IsPlaying)
        {
            Taskbar.ProgressState = TaskbarItemProgressState.Normal;
            Taskbar.ProgressValue = p.Position / p.Duration;
        }
        else
        {
            Taskbar.ProgressState = p.Duration > 0 && p.Book != null ? TaskbarItemProgressState.Paused : TaskbarItemProgressState.None;
            if (p.Duration > 0) Taskbar.ProgressValue = p.Position / p.Duration;
        }
    }
}
