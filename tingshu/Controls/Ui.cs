using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace tingshu.Controls;

/// <summary>样式中使用的附加属性</summary>
public static class Ui
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(8), FrameworkPropertyMetadataOptions.Inherits));
    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius v) => d.SetValue(CornerRadiusProperty, v);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string v) => d.SetValue(PlaceholderProperty, v);

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string v) => d.SetValue(IconProperty, v);

    /// <summary>侧边栏是否显示文字（窄窗口时只显示图标）</summary>
    public static readonly DependencyProperty ShowLabelProperty = DependencyProperty.RegisterAttached(
        "ShowLabel", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetShowLabel(DependencyObject d) => (bool)d.GetValue(ShowLabelProperty);
    public static void SetShowLabel(DependencyObject d, bool v) => d.SetValue(ShowLabelProperty, v);

    /// <summary>ScrollViewer 滚动到底部时执行命令（无限加载）</summary>
    public static readonly DependencyProperty LoadMoreCommandProperty = DependencyProperty.RegisterAttached(
        "LoadMoreCommand", typeof(System.Windows.Input.ICommand), typeof(Ui), new PropertyMetadata(null, OnLoadMoreChanged));
    public static System.Windows.Input.ICommand? GetLoadMoreCommand(DependencyObject d) => (System.Windows.Input.ICommand?)d.GetValue(LoadMoreCommandProperty);
    public static void SetLoadMoreCommand(DependencyObject d, System.Windows.Input.ICommand? v) => d.SetValue(LoadMoreCommandProperty, v);

    private static void OnLoadMoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv || e.OldValue != null) return;
        sv.ScrollChanged += (_, args) =>
        {
            if (sv.ScrollableHeight <= 0 || args.VerticalChange <= 0 && args.ExtentHeightChange == 0) return;
            if (sv.VerticalOffset >= sv.ScrollableHeight - 240)
            {
                var cmd = GetLoadMoreCommand(sv);
                if (cmd?.CanExecute(null) == true) cmd.Execute(null);
            }
        };
    }

    /// <summary>点击按钮时在其下方弹出 ContextMenu</summary>
    public static readonly DependencyProperty DropDownProperty = DependencyProperty.RegisterAttached(
        "DropDown", typeof(ContextMenu), typeof(Ui), new PropertyMetadata(null, OnDropDownChanged));
    public static ContextMenu? GetDropDown(DependencyObject d) => (ContextMenu?)d.GetValue(DropDownProperty);
    public static void SetDropDown(DependencyObject d, ContextMenu? v) => d.SetValue(DropDownProperty, v);

    private static void OnDropDownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button || e.OldValue != null) return;
        button.Click += (_, _) =>
        {
            var menu = GetDropDown(button);
            if (menu == null) return;
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.DataContext = button.DataContext;
            menu.IsOpen = true;
        };
    }
}

/// <summary>旋转加载动画</summary>
public class Spinner : Control
{
    static Spinner()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Spinner), new FrameworkPropertyMetadata(typeof(Spinner)));
    }
}

/// <summary>
/// 自适应网格：按可用宽度自动计算列数，每列等宽拉伸。窗口越宽列数越多。
/// </summary>
public class ResponsiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(ResponsiveGrid), new FrameworkPropertyMetadata(300.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ResponsiveGrid), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(ResponsiveGrid), new FrameworkPropertyMetadata(8, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }

    private int Columns(double width) =>
        double.IsInfinity(width) ? 1 : Math.Clamp((int)((width + Spacing) / (MinItemWidth + Spacing)), 1, MaxColumns);

    private double ItemWidth(double width, int cols) => Math.Max(0, (width - Spacing * (cols - 1)) / cols);

    private readonly List<double> _rowHeights = new();

    protected override Size MeasureOverride(Size available)
    {
        var cols = Columns(available.Width);
        var w = double.IsInfinity(available.Width) ? MinItemWidth : ItemWidth(available.Width, cols);
        _rowHeights.Clear();
        double rowMax = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(w, double.PositiveInfinity));
            rowMax = Math.Max(rowMax, child.DesiredSize.Height);
            if (i % cols == cols - 1 || i == InternalChildren.Count - 1)
            {
                _rowHeights.Add(rowMax);
                rowMax = 0;
            }
        }
        var height = _rowHeights.Sum() + Spacing * Math.Max(0, _rowHeights.Count - 1);
        return new Size(double.IsInfinity(available.Width) ? w : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cols = Columns(final.Width);
        var w = ItemWidth(final.Width, cols);
        double y = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var row = i / cols;
            var col = i % cols;
            if (col == 0 && row > 0) y += _rowHeights[row - 1] + Spacing;
            var h = row < _rowHeights.Count ? _rowHeights[row] : InternalChildren[i].DesiredSize.Height;
            InternalChildren[i].Arrange(new Rect(col * (w + Spacing), y, w, h));
        }
        return final;
    }
}

/// <summary>把图标字体中的字符画成图片（任务栏缩略图按钮用）</summary>
public static class GlyphImage
{
    public static ImageSource Create(string glyph, Brush brush, double size = 32)
    {
        var typeface = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size * 0.75, brush, 1.0);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }
        var image = new DrawingImage(visual.Drawing);
        image.Freeze();
        return image;
    }
}
