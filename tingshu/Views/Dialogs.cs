using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TingShu.Sdk;
using tingshu.Controls;
using tingshu.Core;
using tingshu.Themes;

namespace tingshu.Views;

/// <summary>
/// 与主窗口风格一致的对话框外壳：自定义标题栏 + 圆角 + 主题色
/// </summary>
public class DialogWindow : Window
{
    private readonly ContentControl _body = new() { Focusable = false };

    public DialogWindow(string title)
    {
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "B.Bg");
        SetResourceReference(ForegroundProperty, "B.Text");
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("F.Text");
        FontSize = 13;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(4), GlassFrameThickness = new Thickness(1), UseAeroCaptionButtons = false });
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var close = new Button { Content = "", ToolTip = "关闭" };
        close.SetResourceReference(StyleProperty, "Btn.Close");
        close.Click += (_, _) => Close();
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);

        var titleText = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
        var bar = new DockPanel { Height = 44 };
        DockPanel.SetDock(close, Dock.Right);
        close.VerticalAlignment = VerticalAlignment.Top;
        bar.Children.Add(close);
        bar.Children.Add(titleText);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_body);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    public object Body
    {
        get => _body.Content;
        set => _body.Content = value;
    }

    protected static Button MakeButton(string text, string style, RoutedEventHandler click)
    {
        var b = new Button { Content = text, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
        b.SetResourceReference(StyleProperty, style);
        b.Click += click;
        return b;
    }

    protected static TextBlock MakeText(string text, string style, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
        t.SetResourceReference(StyleProperty, style);
        return t;
    }
}

/// <summary>单行输入对话框</summary>
public sealed class InputDialog : DialogWindow
{
    private readonly TextBox _box;
    private bool _ok;

    private InputDialog(string title, string message, string defaultValue) : base(title)
    {
        _box = new TextBox { Text = defaultValue, Width = 440, Margin = new Thickness(0, 10, 0, 0) };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Confirm();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(MakeButton("取消", "Btn.Secondary", (_, _) => Close()));
        buttons.Children.Add(MakeButton("确定", "Btn.Primary", (_, _) => Confirm()));
        var panel = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
        panel.Children.Add(MakeText(message, "T.Secondary"));
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Body = panel;
        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    private void Confirm()
    {
        _ok = true;
        Close();
    }

    public static string? Show(string title, string message, string defaultValue = "")
    {
        var dialog = new InputDialog(title, message, defaultValue) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
        return dialog._ok ? dialog._box.Text : null;
    }
}

/// <summary>插件设置（IConfigurableSource）</summary>
public sealed class ConfigWindow : DialogWindow
{
    private readonly List<Action> _savers = new();

    public ConfigWindow(SourceEntry entry, IReadOnlyList<ConfigItem> items) : base($"{entry.Name} 设置")
    {
        var host = new SourceHost(entry.Id, entry.Name);
        var panel = new StackPanel { Margin = new Thickness(20, 0, 20, 20), Width = 420 };

        foreach (var item in items)
        {
            switch (item)
            {
                case ConfigItem.Text t:
                {
                    panel.Children.Add(MakeText(t.Label, "T.Body", new Thickness(0, 10, 0, 6)));
                    var box = new TextBox { Text = host.GetPref(t.Key, t.Default) ?? "" };
                    panel.Children.Add(box);
                    _savers.Add(() => host.SetPref(t.Key, box.Text));
                    break;
                }
                case ConfigItem.Switch s:
                {
                    var check = new CheckBox { Content = s.Label, Margin = new Thickness(0, 14, 0, 0), IsChecked = (host.GetPref(s.Key) ?? s.Default.ToString().ToLower()) == "true" };
                    check.SetResourceReference(StyleProperty, "Switch");
                    panel.Children.Add(check);
                    _savers.Add(() => host.SetPref(s.Key, check.IsChecked == true ? "true" : "false"));
                    break;
                }
                case ConfigItem.Select sel:
                {
                    panel.Children.Add(MakeText(sel.Label, "T.Body", new Thickness(0, 10, 0, 6)));
                    var combo = new ComboBox { ItemsSource = sel.Options, SelectedItem = host.GetPref(sel.Key, sel.Default) };
                    panel.Children.Add(combo);
                    _savers.Add(() => host.SetPref(sel.Key, combo.SelectedItem as string));
                    break;
                }
                case ConfigItem.Button b:
                {
                    var btn = MakeButton(b.Label, "Btn.Secondary", (_, _) =>
                    {
                        try { b.Click(); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, b.Label); }
                    });
                    btn.Margin = new Thickness(0, 14, 0, 0);
                    btn.HorizontalAlignment = HorizontalAlignment.Left;
                    panel.Children.Add(btn);
                    break;
                }
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(MakeButton("取消", "Btn.Secondary", (_, _) => Close()));
        buttons.Children.Add(MakeButton("保存", "Btn.Primary", (_, _) =>
        {
            foreach (var save in _savers) save();
            Close();
        }));
        panel.Children.Add(buttons);
        Body = new ScrollViewer { Content = panel, MaxHeight = 640, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

/// <summary>在 WebView 中登录（Cookie 与后台 WebView 共享）</summary>
public sealed class LoginWindow : DialogWindow
{
    /// <summary>autoClosePassed：根据页面标题判断已完成（例如搜索验证通过），满足时自动关闭窗口</summary>
    public LoginWindow(string url, bool desktop, string name, string action = "登录", Func<string, bool>? autoClosePassed = null)
        : base($"{action} {name}")
    {
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Manual;
        Width = desktop ? 1100 : 480;
        Height = 760;

        var tip = MakeText($"在下方页面完成{action}后直接关闭窗口即可，状态会保存下来。", "T.Caption", new Thickness(20, 0, 20, 10));
        var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White };
        var panel = new DockPanel();
        DockPanel.SetDock(tip, Dock.Top);
        panel.Children.Add(tip);
        panel.Children.Add(web);
        Body = panel;

        Loaded += async (_, _) =>
        {
            try
            {
                var env = await WebViewService.Instance.GetEnvironmentAsync();
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Settings.UserAgent = HttpService.UserAgent(desktop);
                web.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    web.CoreWebView2.Navigate(e.Uri);
                };
                if (autoClosePassed != null)
                {
                    web.CoreWebView2.NavigationCompleted += async (_, e) =>
                    {
                        if (!e.IsSuccess || Passed) return;
                        var title = web.CoreWebView2.DocumentTitle ?? "";
                        if (!autoClosePassed(title)) return;
                        Passed = true;
                        tip.Text = $"{action}已完成，窗口即将关闭…";
                        await Task.Delay(1200);
                        Close();
                    };
                }
                web.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                tip.Text = "无法打开登录页面：" + ex.Message + "（请安装 WebView2 运行时）";
            }
        };
        Closed += (_, _) => web.Dispose();
    }

    /// <summary>是否已自动判定为完成</summary>
    public bool Passed { get; private set; }
}
