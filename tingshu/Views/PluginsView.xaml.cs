using System.Collections.Specialized;
using System.Windows.Controls;
using tingshu.ViewModels;

namespace tingshu.Views;

public partial class PluginsView : UserControl
{
    public PluginsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is PluginsViewModel vm)
                vm.Logs.CollectionChanged += ScrollLogs;
            if (e.OldValue is PluginsViewModel old)
                old.Logs.CollectionChanged -= ScrollLogs;
        };
    }

    private void ScrollLogs(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        // 延后执行：此时 ListBox 可能还没处理这次集合变更
        Dispatcher.BeginInvoke(() =>
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
