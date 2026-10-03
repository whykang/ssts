using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace tingshu.Controls;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && (v == Visibility.Visible) != Invert;
}

/// <summary>null / 空字符串 / 0 / 空集合 → Collapsed</summary>
public class EmptyToCollapsedConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            int i => i == 0,
            ICollection c => c.Count == 0,
            _ => false,
        };
        if (Invert) empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
}

/// <summary>值等于参数时为 true（用于 RadioButton 绑定枚举）</summary>
public class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true) return Binding.DoNothing;
        if (targetType.IsEnum) return Enum.Parse(targetType, parameter.ToString()!);
        if (targetType == typeof(double)) return double.Parse(parameter.ToString()!, CultureInfo.InvariantCulture);
        return parameter;
    }
}

/// <summary>取字符串的第一个字符（封面占位）</summary>
public class InitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string ?? "").TrimStart('[', '【', '《', ' ');
        var idx = s.IndexOf(']');
        if (idx >= 0 && idx < s.Length - 1) s = s[(idx + 1)..].Trim();
        return s.Length > 0 ? s[..1] : "听";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>根据字符串生成稳定的渐变色（封面占位背景）</summary>
public class StringToGradientConverter : IValueConverter
{
    private static readonly (string, string)[] Palettes =
    {
        ("#FF8A65", "#F4511E"), ("#64B5F6", "#1E88E5"), ("#81C784", "#43A047"), ("#BA68C8", "#8E24AA"),
        ("#FFD54F", "#FFA000"), ("#4DB6AC", "#00897B"), ("#F06292", "#D81B60"), ("#9575CD", "#5E35B1"),
        ("#4FC3F7", "#0288D1"), ("#A1887F", "#6D4C41"),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        var hash = 0;
        foreach (var c in s) hash = unchecked(hash * 31 + c);
        var (a, b) = Palettes[Math.Abs(hash % Palettes.Length)];
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), 45);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>两个值相等 → true（当前播放章节高亮）</summary>
public class MultiEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var eq = values.Length >= 2 && values[0] != DependencyProperty.UnsetValue && Equals(values[0], values[1]);
        if (targetType == typeof(Visibility)) return eq ? Visibility.Visible : Visibility.Collapsed;
        return eq;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>ListBoxItem → 序号（从 1 开始）</summary>
public class ItemIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Windows.Controls.ListBoxItem item &&
            System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(item) is { } owner)
            return (owner.ItemContainerGenerator.IndexFromContainer(item) + 1).ToString();
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>“作者 · 演播” 组合</summary>
public class JoinConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => string.Join(" · ", values.OfType<string>().Where(s => !string.IsNullOrWhiteSpace(s)));

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>宽度 → 按比例的高度（封面 3:4）</summary>
public class AspectConverter(double ratio) : IValueConverter
{
    public static AspectConverter Portrait { get; } = new(4.0 / 3.0);
    public static AspectConverter Square { get; } = new(1.0);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double w && w > 0 ? w * ratio : double.NaN;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class BoolToGlyphConverter : IValueConverter
{
    public string True { get; set; } = "";
    public string False { get; set; } = "";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? True : False;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
