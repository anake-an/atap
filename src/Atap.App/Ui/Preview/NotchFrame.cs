using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Atap.Core;
using Atap.Core.Placement;

namespace Atap.Ui.Preview;

/// <summary>
/// The notch in the pictures: the same outline as the real notch on the top edge, with its concave shoulders,
/// hanging from a thin strip that stands for the top of the screen. The child sits inside the notch with
/// <see cref="Padding"/> around it. The frame is as wide as the notch, its shoulders and, where there is room,
/// <see cref="EdgeReach"/> on both sides. Given more width, the strip runs across all of it and the notch stays in the middle.
/// Its look comes from the Preview.Notch style in PreviewViews.xaml, set by key, as an implicit style would not
/// reach a frame inside a template.
/// </summary>
public class NotchFrame : Decorator
{
    // The notch starts this far under the strip, so no seam shows where they meet.
    private const double Overlap = 1;

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(NotchFrame),
        new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty NotchMinWidthProperty = DependencyProperty.Register(
        nameof(NotchMinWidth), typeof(double), typeof(NotchFrame),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty NotchMinHeightProperty = DependencyProperty.Register(
        nameof(NotchMinHeight), typeof(double), typeof(NotchFrame),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(NotchState), typeof(NotchFrame),
        new FrameworkPropertyMetadata(NotchState.Idle, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(NotchFrame),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(NotchFrame),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EdgeBrushProperty = DependencyProperty.Register(
        nameof(EdgeBrush), typeof(Brush), typeof(NotchFrame),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EdgeThicknessProperty = DependencyProperty.Register(
        nameof(EdgeThickness), typeof(double), typeof(NotchFrame),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty EdgeReachProperty = DependencyProperty.Register(
        nameof(EdgeReach), typeof(double), typeof(NotchFrame),
        new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _notch;
    private Rect _notchRect = Rect.Empty;

    /// <summary>Space between the notch outline and the child.</summary>
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>Smallest notch width, padding included, without the shoulders.</summary>
    public double NotchMinWidth
    {
        get => (double)GetValue(NotchMinWidthProperty);
        set => SetValue(NotchMinWidthProperty, value);
    }

    /// <summary>Smallest notch height, padding included.</summary>
    public double NotchMinHeight
    {
        get => (double)GetValue(NotchMinHeightProperty);
        set => SetValue(NotchMinHeightProperty, value);
    }

    /// <summary>The state shown. It picks the shoulder radius, as on the real notch.</summary>
    public NotchState State
    {
        get => (NotchState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The notch colour.</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>A 1 pixel line around the notch, or null for none. High contrast sets it.</summary>
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>The colour of the strip that stands for the top of the screen, or null for no strip.</summary>
    public Brush? EdgeBrush
    {
        get => (Brush?)GetValue(EdgeBrushProperty);
        set => SetValue(EdgeBrushProperty, value);
    }

    /// <summary>How tall the strip is.</summary>
    public double EdgeThickness
    {
        get => (double)GetValue(EdgeThicknessProperty);
        set => SetValue(EdgeThicknessProperty, value);
    }

    /// <summary>How far the strip runs past each shoulder when there is room. Its ends fade out over this length.</summary>
    public double EdgeReach
    {
        get => (double)GetValue(EdgeReachProperty);
        set => SetValue(EdgeReachProperty, value);
    }

    private double Edge => Math.Max(0, EdgeThickness);

    /// <summary>
    /// The notch and its shoulders always get their room. The strip past them only takes the width on offer,
    /// so it never squeezes the notch.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        var padding = Padding;
        var padWidth = padding.Left + padding.Right;
        var padHeight = padding.Top + padding.Bottom;
        var shoulders = 2 * NotchShape.ShoulderRoom;
        var child = Size.Empty;
        if (Child is { } element)
        {
            element.Measure(new Size(
                Math.Max(0, constraint.Width - padWidth - shoulders),
                Math.Max(0, constraint.Height - padHeight - Edge)));
            child = element.DesiredSize;
        }

        var width = child.IsEmpty ? 0 : child.Width;
        var height = child.IsEmpty ? 0 : child.Height;
        _notch = new Size(Math.Max(NotchMinWidth, width + padWidth), Math.Max(NotchMinHeight, height + padHeight));
        var bare = _notch.Width + shoulders;
        var reach = Math.Max(0, Math.Min(2 * Math.Max(0, EdgeReach), constraint.Width - bare));
        return new Size(bare + reach, _notch.Height + Edge);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var width = Math.Min(_notch.Width, Math.Max(0, arrangeSize.Width - 2 * NotchShape.ShoulderRoom));
        var height = Math.Min(_notch.Height, Math.Max(0, arrangeSize.Height - Edge));
        var rect = new Rect((arrangeSize.Width - width) / 2, Edge, width, height);

        var padding = Padding;
        Child?.Arrange(new Rect(
            rect.X + padding.Left,
            rect.Y + padding.Top,
            Math.Max(0, rect.Width - padding.Left - padding.Right),
            Math.Max(0, rect.Height - padding.Top - padding.Bottom)));

        if (rect != _notchRect)
        {
            _notchRect = rect;
            InvalidateVisual();
        }
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_notchRect.IsEmpty)
            return;

        // Drawn from the top of the notch body, one overlap under the strip, so the shoulders meet it cleanly.
        var top = Math.Max(0, Edge - Overlap);
        var outline = NotchShape.Outline(
            NotchAnchor.TopCenter,
            _notchRect.Width,
            _notchRect.Bottom - top,
            NotchShape.ShoulderRadiusFor(State),
            NotchShape.ShoulderRoom);
        var pen = Stroke is { } stroke ? new Pen(stroke, 1) : null;
        drawingContext.DrawGeometry(Fill, pen, Geometry(outline, new Vector(_notchRect.X - NotchShape.ShoulderRoom, top)));

        if (EdgeBrush is { } edge && Edge > 0 && RenderSize.Width > 0)
        {
            // The fade stays clear of the shoulders, so they always meet a solid strip.
            var past = (RenderSize.Width - _notchRect.Width) / 2 - NotchShape.ShoulderRoom;
            drawingContext.PushOpacityMask(Fade(RenderSize.Width, Math.Min(EdgeReach, past)));
            drawingContext.DrawRectangle(edge, null, new Rect(0, 0, RenderSize.Width, Edge));
            drawingContext.Pop();
        }
    }

    /// <summary>The outline as a frozen geometry, moved by <paramref name="offset"/>.</summary>
    private static StreamGeometry Geometry(NotchOutline outline, Vector offset)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(outline.Start, offset), isFilled: true, isClosed: true);
            foreach (var segment in outline.Segments)
            {
                if (segment.IsArc)
                {
                    var sweep = segment.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                    context.ArcTo(ToPoint(segment.End, offset), new Size(segment.Radius, segment.Radius), 0, false, sweep, true, false);
                }
                else
                {
                    context.LineTo(ToPoint(segment.End, offset), true, false);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point ToPoint(ShapePoint point, Vector offset) => new Point(point.X, point.Y) + offset;

    /// <summary>Clear at both ends of the strip and solid between, so it reads as an edge that carries on.</summary>
    private static LinearGradientBrush Fade(double width, double reach)
    {
        var stop = Math.Clamp(Math.Max(0, reach) / width, 0, 0.5);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.Black, stop),
                new GradientStop(Colors.Black, 1 - stop),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        brush.Freeze();
        return brush;
    }
}
