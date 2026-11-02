using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Material.Icons;

namespace Rev.Linux.Controls;

/// <summary>
/// A card image, filling the window. Scroll the mouse wheel (or + / −) to zoom, drag to move,
/// double-click to reset, Esc or ✕ to close.
/// </summary>
public sealed class ImageZoom : Panel
{
    private readonly Image picture;
    private readonly Panel stage = new() { Background = Brushes.Transparent, ClipToBounds = true, Cursor = new Cursor(StandardCursorType.SizeAll) };
    private double scale = 1;
    private Vector offset;
    private Point? dragStart;
    private Vector offsetAtDragStart;
    private DialogLayer? layer;

    private ImageZoom(Bitmap image)
    {
        Background = new SolidColorBrush(Color.Parse("#F20E0D0B"));
        Focusable = true;
        picture = new Image { Source = image, Margin = new Thickness(40), RenderTransformOrigin = RelativePoint.TopLeft };
        stage.Children.Add(picture);
        Children.Add(stage);

        var close = Ui.Button("rev secondary round", Ui.Icon(MaterialIconKind.Close)).Named("Close image");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(24);
        ToolTip.SetTip(close, "Close (Esc)");
        close.Click += (_, _) => Close();
        Children.Add(close);
        Children.Add(new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20),
            FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#B3FFFFFF")),
            Text = "Scroll to zoom · drag to move · double-click to reset · Esc to close",
        });

        stage.PointerWheelChanged += (_, e) => ZoomBy(e.Delta.Y > 0 ? 1.2 : 1 / 1.2, e.GetPosition(stage));
        stage.PointerPressed += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                scale = 1;   // double-click: back to normal
                offset = default;
                Apply();
                return;
            }
            dragStart = e.GetPosition(stage);
            offsetAtDragStart = offset;
            e.Pointer.Capture(stage);
        };
        stage.PointerMoved += (_, e) =>
        {
            if (dragStart is not { } start) return;
            offset = offsetAtDragStart + (e.GetPosition(stage) - start);
            Apply();
        };
        stage.PointerReleased += (_, e) =>
        {
            dragStart = null;
            e.Pointer.Capture(null);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.OemPlus or Key.Add) ZoomBy(1.25, Center);
            else if (e.Key is Key.OemMinus or Key.Subtract) ZoomBy(0.8, Center);
            else return;
            e.Handled = true;
        };
    }

    /// <summary>Shows the image over the window that <paramref name="anything"/> is in.</summary>
    public static async Task Show(Visual anything, Bitmap image)
    {
        if (DialogLayer.For(anything) is not { } layer) return;
        var zoom = new ImageZoom(image) { layer = layer };
        var shown = layer.Show(zoom, centred: false);
        zoom.Focus();
        await shown;
    }

    private void Close() => layer?.Close(this);

    private Point Center => new(stage.Bounds.Width / 2, stage.Bounds.Height / 2);

    /// <summary>Zooms around a point on screen, so what's under the mouse stays under the mouse.</summary>
    private void ZoomBy(double factor, Point around)
    {
        double newScale = Math.Clamp(scale * factor, 1, 8);
        factor = newScale / scale;
        var origin = new Point(picture.Bounds.X + offset.X, picture.Bounds.Y + offset.Y);
        offset += new Vector((origin.X - around.X) * (factor - 1), (origin.Y - around.Y) * (factor - 1));
        scale = newScale;
        if (scale == 1) offset = default;
        Apply();
    }

    private void Apply() =>
        picture.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(scale, scale), new TranslateTransform(offset.X, offset.Y) },
        };
}
