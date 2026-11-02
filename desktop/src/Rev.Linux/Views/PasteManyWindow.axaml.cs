using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.Linux.Views;

/// <summary>Paste many cards at once. The button shows how many cards your text will make.</summary>
public partial class PasteManyWindow : Window
{
    private readonly Deck deck;

    public PasteManyWindow() : this(new Deck()) { }   // (for the XAML previewer only)

    public PasteManyWindow(Deck deck)
    {
        InitializeComponent();
        this.deck = deck;
        DeckText.Text = deck.Name;
        PasteBox.TextChanged += (_, _) =>
        {
            int count = Pairs.Count;
            AddButton.Content = $"Add {count.Counted("card")}";
            AddButton.IsEnabled = count > 0;
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => PasteBox.Focus(), DispatcherPriority.Input);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control) Add_Click(this, e);
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    private List<(string Front, string Back)> Pairs => TextTools.ParsePasted(PasteBox.Text ?? "");

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        var pairs = Pairs;
        if (pairs.Count == 0) return;
        App.Database.AddPasted(deck.Id, pairs);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
