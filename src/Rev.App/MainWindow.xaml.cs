using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rev.App.Views;

namespace Rev.App;

/// <summary>The main window. Shows one screen at a time and remembers the way back.</summary>
public partial class MainWindow : Window
{
    /// <summary>The screens you came through, so "back" knows where to go.</summary>
    private readonly Stack<UserControl> history = new();

    public MainWindow()
    {
        InitializeComponent();
        FitOnScreen();
        Show(new HomeView());

        // Coming back to Rev (e.g. the next morning) refreshes the screen's numbers.
        Activated += (_, _) => { if (Screen.Content is IScreen { } screen && ((UserControl)screen).IsLoaded) screen.Refresh(); };
        // Alt+Left goes back, like in a web browser.
        InputBindings.Add(new KeyBinding(new RelayCommand(GoBack), Key.Left, ModifierKeys.Alt));
    }

    /// <summary>The main window of the app, so any screen can open another one.</summary>
    public static MainWindow Current => (MainWindow)Application.Current.MainWindow;

    /// <summary>Opens a new screen on top of the current one.</summary>
    public void Open(UserControl screen)
    {
        if (Screen.Content is UserControl current) history.Push(current);
        Show(screen);
    }

    /// <summary>Returns to the previous screen (and refreshes it, since things may have changed).</summary>
    public void GoBack()
    {
        if (history.Count == 0) return;
        var previous = history.Pop();
        Show(previous);
        if (previous is IScreen screen && previous.IsLoaded) screen.Refresh();
    }

    private void Show(UserControl screen) => Screen.Content = screen;

    /// <summary>On a small or zoomed-in screen, shrinks the window so all of it is visible.</summary>
    private void FitOnScreen()
    {
        var area = SystemParameters.WorkArea;   // the screen minus the taskbar
        Width = Math.Min(Width, area.Width * 0.92);
        Height = Math.Min(Height, area.Height * 0.92);
    }
}
