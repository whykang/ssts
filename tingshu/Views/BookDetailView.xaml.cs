using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using tingshu.ViewModels;

namespace tingshu.Views;

public partial class BookDetailView : UserControl
{
    public BookDetailView()
    {
        InitializeComponent();
        // 打开时自动定位到上次听的章节
        EpisodeList.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (_located || EpisodeList.Items.Count == 0) return;
            _located = true;
            Dispatcher.InvokeAsync(Locate, System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    private bool _located;

    private void Intro_Click(object sender, MouseButtonEventArgs e)
    {
        Intro.MaxHeight = double.IsPositiveInfinity(Intro.MaxHeight) ? 40 : double.PositiveInfinity;
    }

    private void Locate_Click(object sender, RoutedEventArgs e) => Locate();

    private void Locate()
    {
        if (DataContext is not BookDetailViewModel vm) return;
        var url = vm.Player.Book == vm.Record ? vm.Player.CurrentEpisode?.Url : vm.Record.EpisodeUrl;
        var target = vm.Episodes.FirstOrDefault(ep => ep.Url == url);
        if (target == null) return;
        EpisodeList.ScrollIntoView(target);
        EpisodeList.SelectedItem = target;
    }

    private void SettingsPopup_Closed(object sender, EventArgs e)
    {
        SettingsToggle.IsChecked = false;
        (DataContext as BookDetailViewModel)?.SaveBookSettings();
    }
}
