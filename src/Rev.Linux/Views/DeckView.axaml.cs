using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rev.Core.Models;
using Rev.Core.Review;
using Rev.Core.Scheduling;
using Rev.Linux.Controls;

namespace Rev.Linux.Views;

/// <summary>One deck: its numbers, Study now, Add card / Paste many, and the card browser.</summary>
public partial class DeckView : UserControl, IScreen
{
    private readonly Deck deck;

    public DeckView() : this(new Deck()) { }   // (for the XAML previewer only)

    public DeckView(Deck deck)
    {
        InitializeComponent();
        this.deck = deck;
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = new RelayCommand(() => _ = AddCard()) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new RelayCommand(() => Browser.FocusSearch()) });
        Browser.Setup(() => App.Database.GetCards(deck.Id), withDeckNames: false);
        Browser.Changed += (_, _) => UpdateNumbers();
        Loaded += (_, _) => UpdateNumbers();
    }

    /// <summary>The deck this page shows.</summary>
    public Deck Deck => deck;

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
        EmptyText.IsVisible = cards.Count == 0;
        MainWindow.Current?.RefreshSidebar();
        Browser.IsVisible = cards.Count > 0;
    }

    public static string KindName(CardKind kind) => kind switch
    {
        CardKind.Reversed => "Reversed",
        CardKind.Cloze => "Cloze",
        CardKind.TypeIn => "Type-in",
        _ => "Basic",
    };

    // ---- Actions ----


    private void Study_Click(object? sender, RoutedEventArgs e) => MainWindow.Current.Open(new ReviewView(deck));

    private async void CustomStudy_Click(object? sender, RoutedEventArgs e)
    {
        var custom = await new CustomStudyWindow(deck).ShowDialog<CustomStudy?>(MainWindow.Current);
        if (custom != null) MainWindow.Current.Open(new ReviewView(deck, custom));
    }

    private void Export_Click(object? sender, RoutedEventArgs e) => _ = DeckActions.Export(this, deck);

    private void AddCard_Click(object? sender, RoutedEventArgs e) => _ = AddCard();

    private async Task AddCard()
    {
        await new CardEditorWindow(deck, cardToEdit: null).ShowDialog(MainWindow.Current);
        Refresh();
    }

    private async void PasteMany_Click(object? sender, RoutedEventArgs e)
    {
        await new PasteManyWindow(deck).ShowDialog(MainWindow.Current);
        Refresh();
    }
}
