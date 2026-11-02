using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Rev.App.Views;

/// <summary>Shows one card image full screen, with zoom and move.</summary>
public partial class ImageZoomWindow : Window
{
    private Point? dragStart;
    private Point moveAtDragStart;

    public ImageZoomWindow(ImageSource image)
    {
        InitializeComponent();
        Picture.Source = image;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.OemPlus or Key.Add) ZoomBy(1.25, Center);
            else if (e.Key is Key.OemMinus or Key.Subtract) ZoomBy(0.8, Center);
            else return;
            e.Handled = true;
        };
    }

    private Point Center => new(Stage.ActualWidth / 2, Stage.ActualHeight / 2);

    /// <summary>Zooms around a point on screen, so what's under the mouse stays under the mouse.</summary>
    private void ZoomBy(double factor, Point around)
    {
        double newScale = Math.Clamp(Zoom.ScaleX * factor, 1, 8);
        factor = newScale / Zoom.ScaleX;
        var origin = Picture.TranslatePoint(new Point(0, 0), Stage);
        Move.X += (origin.X - around.X) * (factor - 1);
        Move.Y += (origin.Y - around.Y) * (factor - 1);
        Zoom.ScaleX = Zoom.ScaleY = newScale;
        if (newScale == 1) Move.X = Move.Y = 0;
    }

    private void Stage_MouseWheel(object sender, MouseWheelEventArgs e) =>
        ZoomBy(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(Stage));

    private void Stage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Zoom.ScaleX = Zoom.ScaleY = 1;   // double-click: back to normal
            Move.X = Move.Y = 0;
            return;
        }
        dragStart = e.GetPosition(Stage);
        moveAtDragStart = new Point(Move.X, Move.Y);
        Stage.CaptureMouse();
    }

    private void Stage_MouseMove(object sender, MouseEventArgs e)
    {
        if (dragStart is not { } start) return;
        var now = e.GetPosition(Stage);
        Move.X = moveAtDragStart.X + now.X - start.X;
        Move.Y = moveAtDragStart.Y + now.Y - start.Y;
    }

    private void Stage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        dragStart = null;
        Stage.ReleaseMouseCapture();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
