using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Rev.Core.Models;
using Rev.Linux.Controls;
using Rev.Linux.Views;

namespace Rev.Linux;

/// <summary>The main window: the sidebar and the page shown beside it.</summary>
public partial class MainWindow : Window
{
    private bool closingSynced;

    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        SidebarPanel.Refresh();
        ShowHome();

        // Coming back to Rev (e.g. the next morning) refreshes the numbers.
        Activated += (_, _) =>
        {
            RefreshSidebar();
            if (Screen.Content is IScreen screen and Control { IsLoaded: true }) screen.Refresh();
        };
        // Ctrl+1/2/3 Home, All cards, Stats; Ctrl+, Settings; Ctrl+B the sidebar; Ctrl+Shift+N a new deck.
        Bind(Key.D1, KeyModifiers.Control, ShowHome);
        Bind(Key.D2, KeyModifiers.Control, () => Open(new AllCardsView()));
        Bind(Key.D3, KeyModifiers.Control, () => Open(new StatsView()));
        Bind(Key.OemComma, KeyModifiers.Control, () => Open(new SettingsView()));
        Bind(Key.B, KeyModifiers.Control, ToggleSidebar);
        Bind(Key.N, KeyModifiers.Control | KeyModifiers.Shift, () => _ = DeckActions.New(this));

        // Sync with Anki (if it's set up): soon after opening, every few minutes, and when closing.
        Opened += (_, _) =>
        {
            FitOnScreen();
            SyncRunner.Start();
        };
        SyncRunner.Synced += (_, _) =>
        {
            RefreshSidebar();
            if (Screen.Content is IScreen screen && !IsReviewing) screen.Refresh();
        };
        Closing += (_, _) =>
        {
            if (closingSynced) return;
            closingSynced = true;
            SyncRunner.RunQuietly();
        };
    }

    /// <summary>The main window of the app, so any page can show another one.</summary>
    public static MainWindow Current { get; private set; } = null!;

    /// <summary>True while a review is shown (automatic syncs wait until you leave it).</summary>
    public bool IsReviewing => Screen.Content is ReviewView;

    /// <summary>The page shown now (for the automatic tests).</summary>
    public Control? CurrentScreen => Screen.Content as Control;

    public bool IsSidebarOpen => SidebarPanel.IsVisible;

    /// <summary>Shows a page beside the sidebar, in place of the one there now.</summary>
    public void Open(Control page)
    {
        Screen.Content = page;
        SidebarPanel.Highlight(page);
        // Give the keyboard to the new page, so its shortcuts (Space, Ctrl+N…) work at once.
        page.Focusable = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!page.IsKeyboardFocusWithin) page.Focus();
        }, DispatcherPriority.Input);
    }

    public void ShowHome() => Open(new HomeView());

    public void ShowDeck(Deck deck) => Open(new DeckView(deck));

    /// <summary>Reads the decks and their numbers again for the sidebar.</summary>
    public void RefreshSidebar() => SidebarPanel.Refresh();

    public void ToggleSidebar()
    {
        SidebarPanel.IsVisible = !SidebarPanel.IsVisible;
        ShowSidebarButton.IsVisible = !SidebarPanel.IsVisible;
    }

    private void ShowSidebar_Click(object? sender, RoutedEventArgs e) => ToggleSidebar();

    private void Bind(Key key, KeyModifiers modifiers, Action action) =>
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = new RelayCommand(action) });

    /// <summary>On a small or zoomed-in screen, shrinks the window so all of it is visible.</summary>
    private void FitOnScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
        Width = Math.Min(Width, area.Width * 0.92);
        Height = Math.Min(Height, area.Height * 0.92);
    }
}
