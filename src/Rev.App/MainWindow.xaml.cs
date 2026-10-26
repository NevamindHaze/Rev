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
        Activated += (_, _) =>
        {
            if (Screen.Content is not UserControl { IsLoaded: true } screen || screen is not IScreen refreshable) return;
            refreshable.Refresh();
            // If refreshing redrew whatever had the keyboard, give it back to the screen.
            if (!screen.IsKeyboardFocusWithin && IsActive) FocusScreen(screen);
        };
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

    private void Show(UserControl screen)
    {
        Screen.Content = screen;
        // Make sure the new screen has the keyboard, so its shortcuts (Esc, Ctrl+N…) work at once.
        Dispatcher.BeginInvoke(() =>
        {
            if (!screen.IsKeyboardFocusWithin) FocusScreen(screen);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Gives the keyboard to the screen itself rather than its first button: shortcuts work the same,
    /// but no button's tooltip pops up on its own. (Tab still moves to the buttons.)
    /// </summary>
    private static void FocusScreen(UserControl screen)
    {
        screen.Focusable = true;
        screen.FocusVisualStyle = null;
        screen.Focus();
    }

    /// <summary>On a small or zoomed-in screen, shrinks the window so all of it is visible.</summary>
    private void FitOnScreen()
    {
        var area = SystemParameters.WorkArea;   // the screen minus the taskbar
        Width = Math.Min(Width, area.Width * 0.92);
        Height = Math.Min(Height, area.Height * 0.92);
    }
}
