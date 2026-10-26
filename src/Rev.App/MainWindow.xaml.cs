using System.Windows;

namespace Rev.App;

/// <summary>The main window. Shows the Home screen.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        FitOnScreen();
        // e.g. "Monday 26 October", in your computer's language.
        DateText.Text = DateTime.Now.ToString("dddd d MMMM");
    }

    /// <summary>On a small or zoomed-in screen, shrinks the window so all of it is visible.</summary>
    private void FitOnScreen()
    {
        var screen = SystemParameters.WorkArea;   // the screen minus the taskbar
        Width = Math.Min(Width, screen.Width * 0.92);
        Height = Math.Min(Height, screen.Height * 0.92);
    }
}
