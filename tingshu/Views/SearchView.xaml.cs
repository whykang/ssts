using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace tingshu.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Box.Focus();
            Keyboard.Focus(Box);
            Box.CaretIndex = Box.Text.Length;
        };
    }
}
