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
        // Ctrl+N makes a new deck, Ctrl+F searches every card.
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = new RelayCommand(() => _ = DeckActions.New(this)) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new RelayCommand(OpenAllCards) });
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
            tile.RenameRequested += (_, _) => _ = DeckActions.Rename(this, tile.Deck);
            tile.DeleteRequested += (_, _) => _ = DeckActions.Delete(this, tile.Deck);
            tile.OpenRequested += (_, _) => MainWindow.Current.ShowDeck(tile.Deck);
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

    private void Settings_Click(object? sender, RoutedEventArgs e) => MainWindow.Current.Open(new SettingsView());

    private void NewDeck_Click(object? sender, RoutedEventArgs e) => _ = DeckActions.New(this);

    /// <summary>Puts the keyboard back on a deck's tile after a dialog closes.</summary>
    private void FocusTile(Guid deckId) => Tiles.FirstOrDefault(t => t.Deck.Id == deckId)?.Focus(NavigationMethod.Tab);
}
