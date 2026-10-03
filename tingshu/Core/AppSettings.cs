namespace tingshu.Core;

public enum ThemeMode { System, Light, Dark }

public class AppSettings
{
    public const string DefaultDesktopUA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0";
    public const string DefaultMobileUA =
        "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36";

    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public string Accent { get; set; } = "#FF6B4A";
    public string DesktopUA { get; set; } = DefaultDesktopUA;
    public string MobileUA { get; set; } = DefaultMobileUA;
    public double Volume { get; set; } = 0.8;
    public double DefaultSpeed { get; set; } = 1.0;
    public bool AutoPlayNext { get; set; } = true;
    public bool ResumeOnStartup { get; set; } = true;
    public int SearchConcurrency { get; set; } = 6;
    public int SearchTimeoutSeconds { get; set; } = 20;
    public int SeekStepSeconds { get; set; } = 15;

    /// <summary>被禁用的源 ID</summary>
    public HashSet<string> DisabledSources { get; set; } = new();
    /// <summary>手动启用的源 ID（覆盖插件的“默认禁用”）</summary>
    public HashSet<string> EnabledSources { get; set; } = new();
    /// <summary>源排序（源 ID 列表）</summary>
    public List<string> SourceOrder { get; set; } = new();
    /// <summary>插件自己的配置：key = 源ID.键</summary>
    public Dictionary<string, string> SourcePrefs { get; set; } = new();
    public List<string> SearchHistory { get; set; } = new();
    public string? LastDiscoverSource { get; set; }

    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= JsonStore.Load<AppSettings>(AppPaths.SettingsFile);

    private static readonly Debouncer SaveDebouncer = new(TimeSpan.FromMilliseconds(800));
    public void Save() => SaveDebouncer.Run(() => JsonStore.Save(AppPaths.SettingsFile, this));
    public void SaveNow() => JsonStore.Save(AppPaths.SettingsFile, this);
}

public sealed class Debouncer(TimeSpan delay)
{
    private CancellationTokenSource? _cts;

    public void Run(Action action)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Task.Delay(delay, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) action();
        }, TaskScheduler.Default);
    }
}
