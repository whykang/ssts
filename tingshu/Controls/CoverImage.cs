using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using tingshu.Core;

namespace tingshu.Controls;

/// <summary>
/// 封面：圆角裁剪 + 按书名生成的渐变占位 + 异步加载图片
/// </summary>
public sealed class CoverImage : Border
{
    private static readonly StringToGradientConverter GradientConverter = new();
    private static readonly InitialConverter InitialConverter = new();

    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _initial = new()
    {
        Foreground = Brushes.White,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.92,
    };

    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(
        nameof(Url), typeof(string), typeof(CoverImage), new PropertyMetadata(null, (d, _) => ((CoverImage)d).Load()));
    public static readonly DependencyProperty SourceIdProperty = DependencyProperty.Register(
        nameof(SourceId), typeof(string), typeof(CoverImage), new PropertyMetadata(null));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CoverImage), new PropertyMetadata(null, (d, _) => ((CoverImage)d).UpdatePlaceholder()));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(CoverImage), new PropertyMetadata(8.0, (d, _) => ((CoverImage)d).UpdateClip()));

    public string? Url { get => (string?)GetValue(UrlProperty); set => SetValue(UrlProperty, value); }
    public string? SourceId { get => (string?)GetValue(SourceIdProperty); set => SetValue(SourceIdProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public CoverImage()
    {
        var grid = new Grid();
        grid.Children.Add(_initial);
        grid.Children.Add(_image);
        Child = grid;
        SizeChanged += (_, _) =>
        {
            UpdateClip();
            _initial.FontSize = Math.Max(12, Math.Min(ActualWidth, ActualHeight) * 0.42);
        };
        UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
    {
        Background = (Brush)GradientConverter.Convert(Title ?? "", typeof(Brush), null!, null!);
        _initial.Text = (string)InitialConverter.Convert(Title ?? "", typeof(string), null!, null!);
    }

    private void UpdateClip()
    {
        Clip = ActualWidth > 0 ? new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), Radius, Radius) : null;
    }

    private void Load()
    {
        ImageLoader.SetSourceId(_image, SourceId);
        ImageLoader.SetUrl(_image, Url);
    }
}
