using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using tingshu.Core;
using tingshu.Themes;

namespace tingshu.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, IPageActivated
{
    private static AppSettings S => AppSettings.Current;

    public string[] Accents => ThemeManager.Accents;
    public double[] Speeds => PlayerService.SpeedOptions;
    public int[] SeekSteps { get; } = { 5, 10, 15, 30, 60 };

    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    public string WebViewVersion => WebViewService.RuntimeVersion ?? "未安装（WebView 类插件不可用）";
    public string DataDir => AppPaths.DataDir;

    [ObservableProperty] private string _cacheSize = "";

    public ThemeMode Theme
    {
        get => S.Theme;
        set
        {
            if (S.Theme == value) return;
            S.Theme = value;
            S.Save();
            ThemeManager.Apply();
            OnPropertyChanged();
        }
    }

    public string Accent
    {
        get => S.Accent;
        set
        {
            S.Accent = value;
            S.Save();
            ThemeManager.Apply();
            OnPropertyChanged();
        }
    }

    public bool AutoPlayNext
    {
        get => S.AutoPlayNext;
        set { S.AutoPlayNext = value; S.Save(); OnPropertyChanged(); }
    }

    public bool ResumeOnStartup
    {
        get => S.ResumeOnStartup;
        set { S.ResumeOnStartup = value; S.Save(); OnPropertyChanged(); }
    }

    public double DefaultSpeed
    {
        get => S.DefaultSpeed;
        set { S.DefaultSpeed = value; S.Save(); OnPropertyChanged(); }
    }

    public int SeekStep
    {
        get => S.SeekStepSeconds;
        set { S.SeekStepSeconds = value; S.Save(); OnPropertyChanged(); }
    }

    public int SearchConcurrency
    {
        get => S.SearchConcurrency;
        set { S.SearchConcurrency = Math.Clamp(value, 1, 32); S.Save(); OnPropertyChanged(); }
    }

    public string DesktopUA
    {
        get => S.DesktopUA;
        set { S.DesktopUA = string.IsNullOrWhiteSpace(value) ? AppSettings.DefaultDesktopUA : value.Trim(); S.Save(); OnPropertyChanged(); }
    }

    public string MobileUA
    {
        get => S.MobileUA;
        set { S.MobileUA = string.IsNullOrWhiteSpace(value) ? AppSettings.DefaultMobileUA : value.Trim(); S.Save(); OnPropertyChanged(); }
    }

    public void OnActivated() => _ = UpdateCacheSize();

    private async Task UpdateCacheSize()
    {
        var size = await Task.Run(ImageLoader.DiskCacheSize);
        CacheSize = size switch
        {
            < 1024 * 1024 => $"{size / 1024.0:0.#} KB",
            _ => $"{size / 1024.0 / 1024:0.#} MB",
        };
    }

    [RelayCommand]
    private async Task ClearCache()
    {
        await Task.Run(ImageLoader.ClearDiskCache);
        await UpdateCacheSize();
        MainViewModel.Instance.ShowToast("封面缓存已清理");
    }

    [RelayCommand]
    private void SetAccent(string color) => Accent = color;

    [RelayCommand]
    private void ResetUA()
    {
        DesktopUA = AppSettings.DefaultDesktopUA;
        MobileUA = AppSettings.DefaultMobileUA;
    }

    [RelayCommand]
    private void OpenDataDir()
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void InstallWebView()
    {
        try { Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true }); }
        catch { }
    }
}
