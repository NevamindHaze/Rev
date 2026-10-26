using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Rev.App.Controls;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;

namespace Rev.App.Views;

/// <summary>One deck: its numbers, Add card / Paste many, and its cards (click one to edit it).</summary>
public partial class DeckView : UserControl, IScreen
{
    private readonly Deck deck;

    public DeckView(Deck deck)
    {
        InitializeComponent();
        this.deck = deck;
        InputBindings.Add(new KeyBinding(new RelayCommand(AddCard), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MainWindow.Current.GoBack()), Key.Escape, ModifierKeys.None));
        Loaded += (_, _) =>
        {
            Refresh();
            AddCardButton.Focus();   // so the keyboard shortcuts work straight away
        };
    }

    public void Refresh()
    {
        var now = DateTime.Now;
        // The deck may have been renamed meanwhile, so read its name again.
        DeckName.Text = App.Database.GetDecks().FirstOrDefault(d => d.Id == deck.Id)?.Name ?? deck.Name;

        var cards = App.Database.GetCards(deck.Id);
        CardsTile.Value = cards.Count.ToString();
        DueTile.Value = cards.DueCount(now).ToString();
        NewTile.Value = cards.NewCount().ToString();

        // (The full card browser, with search, filters and sorting, comes in step 6 of the plan.)
        CardList.Children.Clear();
        foreach (var card in cards.OrderByDescending(c => c.CreatedAt)) CardList.Children.Add(Row(card));
        EmptyText.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One card in the list: question, answer (shorter, grey) and its kind.</summary>
    private Button Row(Card card)
    {
        var front = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        RichText.Fill(front, CardText.Preview(card.Front));
        var back = new TextBlock { FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        back.SetResourceReference(TextBlock.ForegroundProperty, "MutedInkBrush");
        RichText.Fill(back, CardText.Preview(card.Back));

        var kind = new Border { Style = (Style)FindResource("Badge"), Child = new TextBlock { Text = KindName(card.Kind), FontSize = 13, FontWeight = FontWeights.SemiBold } };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new StackPanel { Children = { front, back } });
        Grid.SetColumn(kind, 1);
        grid.Children.Add(kind);

        var row = new Button { Style = (Style)FindResource("CardRowButton"), Content = grid };
        AutomationProperties.SetName(row, CardText.Preview(card.Front));
        row.Click += (_, _) => EditCard(card);
        return row;
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

    private void EditCard(Card card)
    {
        new CardEditorWindow(deck, card) { Owner = Window.GetWindow(this) }.ShowDialog();
        Refresh();
    }

    private void PasteMany_Click(object sender, RoutedEventArgs e)
    {
        new PasteManyWindow(deck) { Owner = Window.GetWindow(this) }.ShowDialog();
        Refresh();
    }
}
