using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.App.Views;

/// <summary>Paste many cards at once. The button shows how many cards your text will make.</summary>
public partial class PasteManyWindow : Window
{
    private readonly Deck deck;

    public PasteManyWindow(Deck deck)
    {
        InitializeComponent();
        this.deck = deck;
        DeckText.Text = deck.Name;
        Loaded += (_, _) => PasteBox.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) Add_Click(this, e);
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        };
    }

    private List<(string Front, string Back)> Pairs => TextTools.ParsePasted(PasteBox.Text);

    private void PasteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        int count = Pairs.Count;
        AddButton.Content = $"Add {count.Counted("card")}";
        AddButton.IsEnabled = count > 0;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var pairs = Pairs;
        if (pairs.Count == 0) return;
        App.Database.AddPasted(deck.Id, pairs);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
