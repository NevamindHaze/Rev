using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rev.App.Controls;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;

namespace Rev.App.Views;

/// <summary>The Home screen. <see cref="Refresh"/> reloads everything from the database.</summary>
public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // Ctrl+N anywhere on this screen makes a new deck.
        InputBindings.Add(new KeyBinding(new RelayCommand(NewDeck), Key.N, ModifierKeys.Control));
        // Two columns of deck tiles on a wide window, one on a narrow one.
        SizeChanged += (_, _) => DeckGrid.Columns = ActualWidth >= 720 ? 2 : 1;
        Loaded += (_, _) => Refresh();
    }

    private Window? OwnerWindow => Window.GetWindow(this);

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
        DeckGrid.Children.Clear();
        for (int i = 0; i < decks.Count; i++)
        {
            var cards = cardsByDeck[decks[i].Id].ToList();
            var tile = new DeckTile(decks[i], i, cards.Count, cards.DueCount(now), cards.NewCount())
            {
                Margin = new Thickness(7, 0, 7, 14),
            };
            tile.RenameRequested += (_, _) => RenameDeck(tile.Deck);
            tile.DeleteRequested += (_, _) => DeleteDeck(tile.Deck);
            // Opening a deck arrives with the deck screen (step 4 of the plan).
            DeckGrid.Children.Add(tile);
        }
        EmptyState.Visibility = decks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Actions ----

    private void NewDeck_Click(object sender, RoutedEventArgs e) => NewDeck();

    private void NewDeck()
    {
        var name = RevDialog.Prompt(OwnerWindow, "New deck", null, "", "Create", "Deck name");
        if (name is null) return;
        var deck = App.Database.AddDeck(name);
        Refresh();
        FocusTile(deck.Id);
    }

    private void RenameDeck(Deck deck)
    {
        var name = RevDialog.Prompt(OwnerWindow, "Rename deck", null, deck.Name, "Save", "Deck name");
        if (name is null || name == deck.Name) return;
        App.Database.RenameDeck(deck.Id, name);
        Refresh();
        FocusTile(deck.Id);
    }

    private void DeleteDeck(Deck deck)
    {
        int count = App.Database.CountCards(deck.Id);
        bool sure = RevDialog.Confirm(OwnerWindow,
            $"Delete “{deck.Name}”?",
            $"This permanently deletes the deck and its {count.Counted("card")}, including their study progress. " +
            "This can't be undone.",
            "Delete", destructive: true);
        if (!sure) return;
        App.Database.DeleteDeck(deck.Id);
        Refresh();
    }

    /// <summary>Puts the keyboard back on a deck's tile after a dialog closes.</summary>
    private void FocusTile(Guid deckId) =>
        DeckGrid.Children.OfType<DeckTile>().FirstOrDefault(t => t.Deck.Id == deckId)?.FocusTile();
}
