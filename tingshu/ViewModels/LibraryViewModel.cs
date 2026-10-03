using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using tingshu.Core;

namespace tingshu.ViewModels;

public sealed partial class LibraryViewModel : ObservableObject
{
    public ObservableCollection<BookRecord> Favorites => LibraryStore.Instance.Favorites;
    public ObservableCollection<BookRecord> Recent => LibraryStore.Instance.Recent;
    public PlayerService Player => PlayerService.Instance;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Items), nameof(EmptyText))] private string _tab = "favorites";

    public LibraryViewModel()
    {
        // 书架为空但有播放记录时，默认显示最近播放
        if (Favorites.Count == 0 && Recent.Count > 0) _tab = "recent";
    }

    public ObservableCollection<BookRecord> Items => Tab == "recent" ? Recent : Favorites;
    public string EmptyText => Tab == "recent" ? "还没有播放记录" : "书架空空如也，去“发现”或“搜索”找本书吧";

    [RelayCommand]
    private async Task Resume(BookRecord? record)
    {
        if (record == null) return;
        if (Player.Book == record && Player.CurrentEpisode != null)
        {
            Player.TogglePlay();
            return;
        }
        await Player.ResumeAsync(record);
    }

    [RelayCommand]
    private void Remove(BookRecord? record)
    {
        if (record == null) return;
        if (Tab == "recent") LibraryStore.Instance.RemoveFromRecent(record);
        else LibraryStore.Instance.SetFavorite(record, false);
    }

    [RelayCommand]
    private void ToggleFavorite(BookRecord? record)
    {
        if (record != null) LibraryStore.Instance.SetFavorite(record, !record.IsFavorite);
    }
}
