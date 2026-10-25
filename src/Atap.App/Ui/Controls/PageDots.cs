using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;

namespace Atap.Ui.Controls;

/// <summary>
/// One dot per panel page, the current one drawn as a short pill: <c>&lt;controls:PageDots Count="{Binding PageCount}"
/// SelectedIndex="{Binding PageIndex}" /&gt;</c>. Clicking a dot goes to that page. Drawn in one pass with no
/// child elements, and its screen reader name is "Page 2 of 3".
/// </summary>
public class PageDots : FrameworkElement
{
    private const double Dot = 6;
    private const double CurrentWidth = 16;
    private const double Spacing = 6;
    private const double HitHeight = 16;

    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(PageDots),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(PageDots),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(PageDots),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentBrushProperty = DependencyProperty.Register(
        nameof(CurrentBrush), typeof(Brush), typeof(PageDots),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>The current page, from 0.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public Brush DotBrush
    {
        get => (Brush)GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public Brush CurrentBrush
    {
        get => (Brush)GetValue(CurrentBrushProperty);
        set => SetValue(CurrentBrushProperty, value);
    }

    /// <summary>The page under a point, or -1 between dots.</summary>
    public int IndexAt(double x)
    {
        var left = 0.0;
        for (var i = 0; i < Count; i++)
        {
            var width = WidthOf(i);
            if (x >= left - Spacing / 2 && x < left + width + Spacing / 2)
                return i;
            left += width + Spacing;
        }
        return -1;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Count <= 0 ? default : new Size((Count - 1) * (Dot + Spacing) + CurrentWidth, HitHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        // A clear background so the gaps between dots take clicks too.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var top = (RenderSize.Height - Dot) / 2;
        var left = 0.0;
        for (var i = 0; i < Count; i++)
        {
            var width = WidthOf(i);
            drawingContext.DrawRoundedRectangle(
                i == SelectedIndex ? CurrentBrush : DotBrush, null, new Rect(left, top, width, Dot), Dot / 2, Dot / 2);
            left += width + Spacing;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var index = IndexAt(e.GetPosition(this).X);
        if (index >= 0)
            SelectedIndex = index;
        // The notch behind would take the click as open or close the panel.
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PageDotsAutomationPeer(this);

    private double WidthOf(int index) => index == SelectedIndex ? CurrentWidth : Dot;

    private sealed class PageDotsAutomationPeer(PageDots owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(PageDots);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            var dots = (PageDots)Owner;
            return Atap.Core.Localization.Localizer.Instance.Format(KitText.Page, ("page", dots.SelectedIndex + 1), ("count", dots.Count));
        }
    }
}
