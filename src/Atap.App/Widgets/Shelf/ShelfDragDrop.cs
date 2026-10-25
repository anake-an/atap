using System.Windows;
using System.Windows.Input;

namespace Atap.Widgets.Shelf;

/// <summary>
/// Attached properties for the shelf views, which are plain resource dictionaries with no code-behind.
/// DropTarget takes files dropped from Explorer. DragPath lets a row be dragged out as a file.
/// </summary>
public static class ShelfDragDrop
{
    public static readonly DependencyProperty DropTargetProperty = DependencyProperty.RegisterAttached(
        "DropTarget", typeof(FileShelfViewModel), typeof(ShelfDragDrop), new PropertyMetadata(null, OnDropTargetChanged));

    public static readonly DependencyProperty DragPathProperty = DependencyProperty.RegisterAttached(
        "DragPath", typeof(string), typeof(ShelfDragDrop), new PropertyMetadata(null, OnDragPathChanged));

    private static readonly DependencyProperty PressedAtProperty = DependencyProperty.RegisterAttached(
        "PressedAt", typeof(Point?), typeof(ShelfDragDrop), new PropertyMetadata(null));

    public static FileShelfViewModel? GetDropTarget(DependencyObject element) => (FileShelfViewModel?)element.GetValue(DropTargetProperty);

    public static void SetDropTarget(DependencyObject element, FileShelfViewModel? value) => element.SetValue(DropTargetProperty, value);

    public static string? GetDragPath(DependencyObject element) => (string?)element.GetValue(DragPathProperty);

    public static void SetDragPath(DependencyObject element, string? value) => element.SetValue(DragPathProperty, value);

    private static void OnDropTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;
        element.DragOver -= OnDragOver;
        element.Drop -= OnDrop;
        element.AllowDrop = e.NewValue is not null;
        if (e.NewValue is null)
            return;
        element.DragOver += OnDragOver;
        element.Drop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject d && GetDropTarget(d) is { } shelf && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            shelf.Drop(paths);
        e.Handled = true;
    }

    private static void OnDragPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;
        element.PreviewMouseLeftButtonDown -= OnPressed;
        element.PreviewMouseMove -= OnMoved;
        if (e.NewValue is null)
            return;
        element.PreviewMouseLeftButtonDown += OnPressed;
        element.PreviewMouseMove += OnMoved;
    }

    private static void OnPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element)
            element.SetValue(PressedAtProperty, e.GetPosition(element));
    }

    // A drag starts once the mouse moves past the system distance with the button held.
    private static void OnMoved(object sender, MouseEventArgs e)
    {
        if (sender is not UIElement element || element.GetValue(PressedAtProperty) is not Point start)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            element.ClearValue(PressedAtProperty);
            return;
        }
        var delta = e.GetPosition(element) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        element.ClearValue(PressedAtProperty);
        if (GetDragPath(element) is { } path && FileShelfDescriptor.PathExists(path))
            DragDrop.DoDragDrop(element, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }
}
