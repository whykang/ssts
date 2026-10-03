using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using tingshu.Core;

namespace tingshu.Themes;

public static class ThemeManager
{
    public static readonly string[] Accents = { "#FF6B4A", "#3B82F6", "#10B981", "#8B5CF6", "#F59E0B", "#EC4899", "#06B6D4" };

    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    public static void Initialize()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && AppSettings.Current.Theme == ThemeMode.System)
                Application.Current.Dispatcher.Invoke(Apply);
        };
        Apply();
    }

    public static void Apply()
    {
        var settings = AppSettings.Current;
        IsDark = settings.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDark(),
        };

        var resources = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/TingShu;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml") };
        if (resources.Count > 0) resources[0] = palette;
        else resources.Insert(0, palette);

        var accent = ParseColor(settings.Accent) ?? (Color)ColorConverter.ConvertFromString(Accents[0]);
        SetBrush("B.Accent", accent);
        SetBrush("B.AccentHover", Shift(accent, IsDark ? 0.12 : -0.08));
        SetBrush("B.AccentPressed", Shift(accent, IsDark ? -0.1 : -0.16));
        SetBrush("B.AccentSoft", Color.FromArgb(IsDark ? (byte)0x33 : (byte)0x22, accent.R, accent.G, accent.B));
        SetBrush("B.OnAccent", Colors.White);
        Application.Current.Resources["C.Accent"] = accent;

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke();
    }

    private static void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    private static Color Shift(Color c, double amount)
    {
        byte F(byte v) => (byte)Math.Clamp(amount >= 0 ? v + (255 - v) * amount : v * (1 + amount), 0, 255);
        return Color.FromRgb(F(c.R), F(c.G), F(c.B));
    }

    public static Color? ParseColor(string? s)
    {
        try { return s == null ? null : (Color)ColorConverter.ConvertFromString(s); }
        catch { return null; }
    }

    private static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    // ---------- Windows 11 圆角 / 深色边框（Windows 10 上自动忽略） ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));      // DWMWA_USE_IMMERSIVE_DARK_MODE
        var round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));     // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
    }
}
