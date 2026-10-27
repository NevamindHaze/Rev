using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Rev.Linux.Views;

namespace Rev.Linux;

/// <summary>The main window. Shows one screen at a time and remembers the way back.</summary>
public partial class MainWindow : Window
{
    /// <summary>The screens you came through, so "back" knows where to go.</summary>
    private readonly Stack<Control> history = new();
    private bool closingSynced;

    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        Show(new HomeView());

        // Coming back to Rev (e.g. the next morning) refreshes the screen's numbers.
        Activated += (_, _) =>
        {
            if (Screen.Content is IScreen screen and Control { IsLoaded: true }) screen.Refresh();
        };
        // Alt+Left goes back, like in a web browser.
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Left, KeyModifiers.Alt), Command = new RelayCommand(GoBack) });

        // Sync with Anki (if it's set up): soon after opening, every few minutes, and when closing.
        Opened += (_, _) =>
        {
            FitOnScreen();
            SyncRunner.Start();
        };
        SyncRunner.Synced += (_, _) => { if (Screen.Content is IScreen screen && !IsReviewing) screen.Refresh(); };
        Closing += (_, _) =>
        {
            if (closingSynced) return;
            closingSynced = true;
            SyncRunner.RunQuietly();
        };
    }

    /// <summary>The main window of the app, so any screen can open another one.</summary>
    public static MainWindow Current { get; private set; } = null!;

    /// <summary>True while the review screen is open (automatic syncs wait until you leave it).</summary>
    public bool IsReviewing => Screen.Content is ReviewView;

    /// <summary>The screen shown now (for the automatic tests).</summary>
    public Control? CurrentScreen => Screen.Content as Control;

    /// <summary>Opens a new screen on top of the current one.</summary>
    public void Open(Control screen)
    {
        if (Screen.Content is Control current) history.Push(current);
        Show(screen);
    }

    /// <summary>Returns to the previous screen (and refreshes it, since things may have changed).</summary>
    public void GoBack()
    {
        if (history.Count == 0) return;
        var previous = history.Pop();
        Show(previous);
        if (previous is IScreen screen) screen.Refresh();
    }

    private void Show(Control screen)
    {
        Screen.Content = screen;
        // Give the keyboard to the new screen, so its shortcuts (Esc, Ctrl+N…) work at once.
        screen.Focusable = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!screen.IsKeyboardFocusWithin) screen.Focus();
        }, DispatcherPriority.Input);
    }

    /// <summary>On a small or zoomed-in screen, shrinks the window so all of it is visible.</summary>
    private void FitOnScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
        Width = Math.Min(Width, area.Width * 0.92);
        Height = Math.Min(Height, area.Height * 0.92);
    }
}
