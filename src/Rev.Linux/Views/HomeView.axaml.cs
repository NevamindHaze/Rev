using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;
using Rev.Linux.Controls;

namespace Rev.Linux.Views;

/// <summary>The Home screen. <see cref="Refresh"/> reloads everything from the database.</summary>
public partial class HomeView : UserControl, IScreen
{
    public HomeView()
    {
        InitializeComponent();
        // Ctrl+N makes a new deck, Ctrl+F searches every card, Ctrl+, opens Settings.
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = new RelayCommand(() => _ = NewDeck()) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new RelayCommand(OpenAllCards) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Control), Command = new RelayCommand(OpenSettings) });
        // Two columns of deck tiles on a wide window, one on a narrow one.
        SizeChanged += (_, e) => DeckGrid.Columns = e.NewSize.Width >= 720 ? 2 : 1;
        Loaded += (_, _) => Refresh();
    }

    /// <summary>Reads the decks, cards and today's numbers again and redraws the screen.</summary>
    public void Refresh()
    {
        var db = App.Database;
        var now = DateTime.Now;
        string name = db.GetSettings().UserName;

        GreetingText.Text = name.Length == 0 ? "Hi there 👋" : $"Hi, {name} 👋";
        DateText.Text = now.ToString("dddd d MMMM");   // e.g. "Monday 26 October"

        var allCards = db.GetCards();
        DueTile.Value = allCards.DueCount(now).ToString();
        NewTile.Value = allCards.NewCount().ToString();
        DoneTile.Value = db.ReviewsToday(now).ToString();

        var cardsByDeck = allCards.ToLookup(c => c.DeckId);
        var decks = db.GetDecks();
        // Redrawing replaces the tiles; remember which one had the keyboard, to give it back after.
        var focusedDeck = Tiles.FirstOrDefault(t => t.IsKeyboardFocusWithin)?.Deck.Id;
        DeckGrid.Children.Clear();
        for (int i = 0; i < decks.Count; i++)
        {
            var cards = cardsByDeck[decks[i].Id].ToList();
            var tile = new DeckTile(decks[i], i, cards.Count, cards.DueCount(now), cards.NewCount())
            {
                Margin = new Thickness(7, 0, 7, 14),
            };
            tile.RenameRequested += (_, _) => _ = RenameDeck(tile.Deck);
            tile.DeleteRequested += (_, _) => _ = DeleteDeck(tile.Deck);
            tile.OpenRequested += (_, _) => MainWindow.Current.Open(new DeckView(tile.Deck));
            DeckGrid.Children.Add(tile);
        }
        EmptyState.IsVisible = decks.Count == 0;
        if (focusedDeck is { } id) FocusTile(id);
    }

    private IEnumerable<DeckTile> Tiles => DeckGrid.Children.OfType<DeckTile>();

    // ---- Actions ----

    private void ReviewAll_Click(object? sender, RoutedEventArgs e) => MainWindow.Current.Open(new ReviewView(null));

    private void AllCards_Click(object? sender, RoutedEventArgs e) => OpenAllCards();

    private void OpenAllCards() => MainWindow.Current.Open(new AllCardsView());

    private void Stats_Click(object? sender, RoutedEventArgs e) => MainWindow.Current.Open(new StatsView());

    private void Settings_Click(object? sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings() => MainWindow.Current.Open(new SettingsView());

    private void NewDeck_Click(object? sender, RoutedEventArgs e) => _ = NewDeck();

    private async Task NewDeck()
    {
        var name = await RevDialog.Prompt(this, "New deck", null, "", "Create", "Deck name");
        if (name is null) return;
        var deck = App.Database.AddDeck(name);
        Refresh();
        FocusTile(deck.Id);
    }

    private async Task RenameDeck(Deck deck)
    {
        var name = await RevDialog.Prompt(this, "Rename deck", null, deck.Name, "Save", "Deck name");
        if (name is null || name == deck.Name) return;
        App.Database.RenameDeck(deck.Id, name);
        Refresh();
        FocusTile(deck.Id);
    }

    private async Task DeleteDeck(Deck deck)
    {
        int count = App.Database.CountCards(deck.Id);
        bool sure = await RevDialog.Confirm(this,
            $"Delete “{deck.Name}”?",
            $"This permanently deletes the deck and its {count.Counted("card")}, including their study progress. " +
            "This can't be undone.",
            "Delete", destructive: true);
        if (!sure) return;
        int place = Tiles.ToList().FindIndex(t => t.Deck.Id == deck.Id);
        App.Database.DeleteDeck(deck.Id);
        Refresh();

        // The deleted tile had the keyboard; give it to the tile now in its place (or New deck),
        // so shortcuts keep working without a click.
        var tiles = Tiles.ToList();
        if (tiles.Count > 0) tiles[Math.Clamp(place, 0, tiles.Count - 1)].Focus(NavigationMethod.Tab);
        else NewDeckButton.Focus(NavigationMethod.Tab);
    }

    /// <summary>Puts the keyboard back on a deck's tile after a dialog closes.</summary>
    private void FocusTile(Guid deckId) => Tiles.FirstOrDefault(t => t.Deck.Id == deckId)?.Focus(NavigationMethod.Tab);
}
