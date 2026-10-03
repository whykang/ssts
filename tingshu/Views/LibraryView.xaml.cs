using System.Windows;
using System.Windows.Controls;
using tingshu.ViewModels;

namespace tingshu.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    private void GoDiscover(object sender, RoutedEventArgs e) => MainViewModel.Instance.NavKey = "discover";

    private void GoSearch(object sender, RoutedEventArgs e) => MainViewModel.Instance.NavKey = "search";
}
