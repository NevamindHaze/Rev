using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rev.Core.Scheduling;
using Rev.Linux.Controls;

namespace Rev.Linux.Views;

/// <summary>The Home page. <see cref="Refresh"/> reloads everything from the database.</summary>
public partial class HomeView : UserControl, IScreen
{
    public HomeView()
    {
        InitializeComponent();
        DeckHeading.ColumnDefinitions = new ColumnDefinitions(DeckListRow.Columns);
        // Ctrl+N makes a new deck, Ctrl+F searches every card.
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = new RelayCommand(() => _ = DeckActions.New(this)) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new RelayCommand(() => MainWindow.Current.Open(new AllCardsView())) });
        Loaded += (_, _) => Refresh();
    }

    /// <summary>Reads the decks, cards and today's numbers again and redraws the page.</summary>
    public void Refresh()
    {
        var db = App.Database;
        var now = DateTime.Now;
        string name = db.GetSettings().UserName;

        GreetingText.Text = name.Length == 0 ? "Hi there 👋" : $"Hi, {name} 👋";
        var allCards = db.GetCards();
        // e.g. "Thursday 29 October · 0 due · 7 new · 0 done today"
        SummaryText.Text = $"{now:dddd d MMMM} · {allCards.DueCount(now)} due · {allCards.NewCount()} new · {db.ReviewsToday(now)} done today";

        var cardsByDeck = allCards.ToLookup(c => c.DeckId);
        var decks = db.GetDecks();
        // Redrawing replaces the rows; remember which one had the keyboard, to give it back after.
        var focusedDeck = Rows.FirstOrDefault(r => r.IsKeyboardFocusWithin)?.Deck.Id;
        DeckRows.Children.Clear();
        for (int i = 0; i < decks.Count; i++)
        {
            var cards = cardsByDeck[decks[i].Id].ToList();
            DeckRows.Children.Add(new DeckListRow(decks[i], i, cards.DueCount(now), cards.NewCount(), cards.Count));
        }
        DeckTable.IsVisible = decks.Count > 0;
        EmptyState.IsVisible = decks.Count == 0;
        if (focusedDeck is { } id) Rows.FirstOrDefault(r => r.Deck.Id == id)?.Focus(NavigationMethod.Tab);
        MainWindow.Current?.RefreshSidebar();
    }

    private IEnumerable<DeckListRow> Rows => DeckRows.Children.OfType<DeckListRow>();

    private void ReviewAll_Click(object? sender, RoutedEventArgs e) => MainWindow.Current.Open(new ReviewView(null));

    private void NewDeck_Click(object? sender, RoutedEventArgs e) => _ = DeckActions.New(this);
}
