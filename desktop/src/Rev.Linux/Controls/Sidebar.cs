using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Linux.Views;

namespace Rev.Linux.Controls;

/// <summary>
/// The column down the left of the main window: Home, All cards and Stats, then your decks with how many
/// cards are waiting today, then Settings and when Rev last synced with Anki. The page you're on is highlighted.
/// </summary>
public sealed class Sidebar : Border
{
    private readonly Button home = NavButton(MaterialIconKind.HomeOutline, "Home", "Ctrl+1");
    private readonly Button allCards = NavButton(MaterialIconKind.Magnify, "All cards", "Ctrl+2");
    private readonly Button stats = NavButton(MaterialIconKind.ChartBar, "Stats", "Ctrl+3");
    private readonly Button settings = NavButton(MaterialIconKind.CogOutline, "Settings", "Ctrl+,");
    private readonly StackPanel deckList = new();
    private readonly TextBlock syncText = new() { FontSize = 12, Margin = new Thickness(10, 2, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private Control? page;

    public Sidebar()
    {
        Width = 220;
        Padding = new Thickness(8, 12, 8, 10);
        this.Colour(BackgroundProperty, "SoftFillBrush");
        syncText.Colour(TextBlock.ForegroundProperty, "MutedInkBrush");

        var collapse = Ui.Button("rev toolbar round", Ui.Icon(MaterialIconKind.DockLeft, 16)).Named("Collapse sidebar");
        ToolTip.SetTip(collapse, "Hide the sidebar (Ctrl+B)");
        collapse.Click += (_, _) => MainWindow.Current.ToggleSidebar();
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 0, 0, 10) };
        top.Children.Add(new TextBlock { Text = "Rev", FontSize = 18, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(collapse, 1);
        top.Children.Add(collapse);

        var newDeck = Ui.Button("rev toolbar round", Ui.Icon(MaterialIconKind.Plus, 16)).Named("New deck");
        newDeck.Width = newDeck.Height = 26;
        ToolTip.SetTip(newDeck, "New deck (Ctrl+Shift+N)");
        newDeck.Click += (_, _) => _ = DeckActions.New(this);
        var decksHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 16, 0, 4) };
        decksHeading.Children.Add(new TextBlock { Text = "Decks", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            .Colour(TextBlock.ForegroundProperty, "MutedInkBrush"));
        Grid.SetColumn(newDeck, 1);
        decksHeading.Children.Add(newDeck);

        home.Click += (_, _) => MainWindow.Current.ShowHome();
        allCards.Click += (_, _) => MainWindow.Current.Open(new AllCardsView());
        stats.Click += (_, _) => MainWindow.Current.Open(new StatsView());
        settings.Click += (_, _) => MainWindow.Current.Open(new SettingsView());

        // Top part, the decks (which scroll when there are many), and Settings at the bottom.
        var layout = new DockPanel();
        var header = new StackPanel { Children = { top, home, allCards, stats, decksHeading } };
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { settings, syncText } };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = deckList, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Child = layout;
    }

    private IEnumerable<SidebarDeckRow> DeckRows => deckList.Children.OfType<SidebarDeckRow>();

    /// <summary>Reads the decks and their numbers again (after a review, an edit, an import or a sync).</summary>
    public void Refresh()
    {
        var now = DateTime.Now;
        var cardsByDeck = App.Database.GetCards().ToLookup(c => c.DeckId);
        var decks = App.Database.GetDecks();
        var focused = DeckRows.FirstOrDefault(r => r.IsKeyboardFocusWithin)?.Deck.Id;
        deckList.Children.Clear();
        for (int i = 0; i < decks.Count; i++)
        {
            var cards = cardsByDeck[decks[i].Id].ToList();
            deckList.Children.Add(new SidebarDeckRow(decks[i], i, cards.DueCount(now) + cards.NewCount()));
        }
        if (focused is { } id) DeckRows.FirstOrDefault(r => r.Deck.Id == id)?.Focus(NavigationMethod.Tab);

        syncText.Text = !SyncRunner.IsOn ? ""
            : SyncRunner.LastTime is { } time ? $"Synced with Anki at {time:HH:mm}" : "Syncing with Anki";
        syncText.IsVisible = syncText.Text.Length > 0;
        Highlight(page);
    }

    /// <summary>Highlights the item for the page shown now (the deck's row for a deck or a review).</summary>
    public void Highlight(Control? shown)
    {
        page = shown;
        SetSelected(home, shown is HomeView);
        SetSelected(allCards, shown is AllCardsView);
        SetSelected(stats, shown is StatsView);
        SetSelected(settings, shown is SettingsView);
        Guid? deckId = shown switch
        {
            DeckView deckPage => deckPage.Deck.Id,
            ReviewView review => review.Deck?.Id,
            _ => null,
        };
        foreach (var row in DeckRows)
        {
            SetSelected(row, row.Deck.Id == deckId);
            row.IsStudying = shown is ReviewView && row.Deck.Id == deckId;
        }
    }

    private static void SetSelected(Button button, bool selected)
    {
        if (selected) button.Classes.Add("selected");
        else button.Classes.Remove("selected");
    }

    private static Button NavButton(MaterialIconKind icon, string text, string shortcut)
    {
        var button = Ui.Button("rev nav", Ui.IconAndText(icon, text, 16, 10)).Named(text);
        ToolTip.SetTip(button, $"{text} ({shortcut})");
        return button;
    }
}

/// <summary>
/// A deck in the sidebar: its colour, its name and how many cards are waiting today (due + new).
/// Click (or Enter) opens it; F2 renames, Delete deletes; right-click also offers Study and Export to Anki.
/// </summary>
public sealed class SidebarDeckRow : Button
{
    private readonly Control playIcon = Ui.Icon(MaterialIconKind.Play, 14);
    private readonly TextBlock countText;

    public Deck Deck { get; }

    /// <summary>Cards waiting today: due ones plus new ones.</summary>
    public int ToStudyCount { get; }

    protected override Type StyleKeyOverride => typeof(Button);

    public SidebarDeckRow(Deck deck, int index, int toStudyCount)
    {
        Deck = deck;
        ToStudyCount = toStudyCount;
        Classes.Add("rev");
        Classes.Add("nav");
        this.Named(deck.Name);

        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }
            .Colour(Shape.FillProperty, Ui.DeckColours(index).Background);
        var name = new TextBlock { Text = deck.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        countText = new TextBlock { Text = toStudyCount > 0 ? toStudyCount.ToString() : "", FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        countText.Colour(TextBlock.ForegroundProperty, "MutedInkBrush");
        playIcon.IsVisible = false;
        playIcon.Margin = new Thickness(8, 0, 0, 0);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        grid.Children.Add(dot);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        Grid.SetColumn(countText, 2);
        grid.Children.Add(countText);
        Grid.SetColumn(playIcon, 3);
        grid.Children.Add(playIcon);
        Content = grid;
        ToolTip.SetTip(this, $"{deck.Name}: {toStudyCount} to study today");

        var study = new MenuItem { Header = "Study" };
        study.Click += (_, _) => MainWindow.Current.Open(new ReviewView(Deck));
        var rename = new MenuItem { Header = "Rename", InputGesture = new KeyGesture(Key.F2) };
        rename.Click += (_, _) => _ = DeckActions.Rename(this, Deck);
        var export = new MenuItem { Header = "Export to Anki…" };
        export.Click += (_, _) => _ = DeckActions.Export(this, Deck);
        var delete = new MenuItem { Header = "Delete", InputGesture = new KeyGesture(Key.Delete) };
        delete.Click += (_, _) => _ = DeckActions.Delete(this, Deck);
        ContextMenu = new ContextMenu { Items = { study, rename, export, new Separator(), delete } };

        Click += (_, _) => MainWindow.Current.ShowDeck(Deck);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F2) _ = DeckActions.Rename(this, Deck);
            else if (e.Key == Key.Delete) _ = DeckActions.Delete(this, Deck);
            else return;
            e.Handled = true;
        };
    }

    /// <summary>True while this deck is being studied: shows a small play symbol.</summary>
    public bool IsStudying
    {
        set
        {
            playIcon.IsVisible = value;
            countText.IsVisible = !value;
        }
    }
}
