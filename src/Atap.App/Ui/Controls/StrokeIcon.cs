using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace Atap.Ui.Controls;

/// <summary>
/// A stroke icon: an "icon.&lt;name&gt;" geometry from Icons.xaml, drawn on a 24 by 24 grid and scaled to its size,
/// 16 by 16 unless set. The stroke takes the inherited Foreground, so it follows the button or card around it,
/// high contrast included. Decorative unless it has an AutomationProperties.Name.
/// </summary>
public sealed class StrokeIcon : FrameworkElement
{
    /// <summary>The grid every icon geometry is drawn on.</summary>
    public const double Grid = 24;

    public const double DefaultSize = 16;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(Geometry),
        typeof(StrokeIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(StrokeIcon),
        new FrameworkPropertyMetadata(
            SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>In grid units, so the line gets thinner and thicker with the icon. 2 by default.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(StrokeIcon),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double d && d >= 0 && !double.IsInfinity(d));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(DefaultSize, DefaultSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var data = Data;
        var brush = Foreground;
        var size = Math.Min(ActualWidth, ActualHeight);
        if (data is null || brush is null || size <= 0)
            return;

        // The pen is scaled with the geometry, as in an SVG view box.
        var pen = new Pen(brush, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        if (pen.CanFreeze)
            pen.Freeze();

        var scale = size / Grid;
        drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale, (ActualWidth - size) / 2, (ActualHeight - size) / 2));
        drawingContext.DrawGeometry(null, pen, data);
        drawingContext.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StrokeIconAutomationPeer(this);

    private sealed class StrokeIconAutomationPeer(StrokeIcon owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(StrokeIcon);

        // An unnamed icon is decoration: the button or card around it carries the name.
        protected override bool IsControlElementCore() => IsNamed && base.IsControlElementCore();

        protected override bool IsContentElementCore() => IsNamed && base.IsContentElementCore();

        private bool IsNamed => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(Owner));
    }
}
