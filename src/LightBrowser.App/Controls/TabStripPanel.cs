using System.Windows;
using System.Windows.Controls;

namespace SudyBrowser.App.Controls;

/// <summary>
/// Lays tabs out in one row with a fixed gap. Tabs are <see cref="MaxTabWidth"/> wide and shrink evenly
/// (down to <see cref="MinTabWidth"/>) when the row runs out of space, so the strip never overflows.
/// </summary>
public sealed class TabStripPanel : Panel
{
    public static readonly DependencyProperty MaxTabWidthProperty = DependencyProperty.Register(
        nameof(MaxTabWidth),
        typeof(double),
        typeof(TabStripPanel),
        new FrameworkPropertyMetadata(148d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinTabWidthProperty = DependencyProperty.Register(
        nameof(MinTabWidth),
        typeof(double),
        typeof(TabStripPanel),
        new FrameworkPropertyMetadata(56d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(TabStripPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MaxTabWidth
    {
        get => (double)GetValue(MaxTabWidthProperty);
        set => SetValue(MaxTabWidthProperty, value);
    }

    public double MinTabWidth
    {
        get => (double)GetValue(MinTabWidthProperty);
        set => SetValue(MinTabWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        var tabWidth = GetTabWidth(availableSize.Width, count);
        var height = 0d;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(tabWidth, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var width = count == 0 ? 0 : (tabWidth * count) + (Spacing * (count - 1));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tabWidth = GetTabWidth(finalSize.Width, InternalChildren.Count);
        var x = 0d;

        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, tabWidth, finalSize.Height));
            x += tabWidth + Spacing;
        }

        return finalSize;
    }

    private double GetTabWidth(double availableWidth, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        if (double.IsInfinity(availableWidth))
        {
            return MaxTabWidth;
        }

        var fit = (availableWidth - (Spacing * (count - 1))) / count;
        return Math.Max(MinTabWidth, Math.Min(MaxTabWidth, fit));
    }
}
