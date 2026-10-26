using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rev.Core.Models;
using Rev.Core.Scheduling;

namespace Rev.App.Views;

/// <summary>One deck: its numbers, Study now, Add card / Paste many, and the card browser.</summary>
public partial class DeckView : UserControl, IScreen
{
    private readonly Deck deck;

    public DeckView(Deck deck)
    {
        InitializeComponent();
        this.deck = deck;
        InputBindings.Add(new KeyBinding(new RelayCommand(AddCard), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Browser.FocusSearch()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MainWindow.Current.GoBack()), Key.Escape, ModifierKeys.None));
        Browser.Setup(() => App.Database.GetCards(deck.Id), withDeckNames: false);
        Browser.Changed += (_, _) => UpdateNumbers();
        Loaded += (_, _) =>
        {
            UpdateNumbers();
            AddCardButton.Focus();   // so the keyboard shortcuts work straight away
        };
    }

    public void Refresh()
    {
        UpdateNumbers();
        Browser.Refresh();
    }

    /// <summary>The deck's name and its Cards / Due / New tiles.</summary>
    private void UpdateNumbers()
    {
        var now = DateTime.Now;
        // The deck may have been renamed meanwhile, so read its name again.
        DeckName.Text = App.Database.GetDecks().FirstOrDefault(d => d.Id == deck.Id)?.Name ?? deck.Name;
        var cards = App.Database.GetCards(deck.Id);
        CardsTile.Value = cards.Count.ToString();
        DueTile.Value = cards.DueCount(now).ToString();
        NewTile.Value = cards.NewCount().ToString();
        EmptyText.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Browser.Visibility = cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public static string KindName(CardKind kind) => kind switch
    {
        CardKind.Reversed => "Reversed",
        CardKind.Cloze => "Cloze",
        CardKind.TypeIn => "Type-in",
        _ => "Basic",
    };

    // ---- Actions ----

    private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.GoBack();

    private void Study_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Open(new ReviewView(deck));

    private void AddCard_Click(object sender, RoutedEventArgs e) => AddCard();

    private void AddCard()
    {
        new CardEditorWindow(deck, cardToEdit: null) { Owner = Window.GetWindow(this) }.ShowDialog();
        Refresh();
    }

    private void PasteMany_Click(object sender, RoutedEventArgs e)
    {
        new PasteManyWindow(deck) { Owner = Window.GetWindow(this) }.ShowDialog();
        Refresh();
    }
}
