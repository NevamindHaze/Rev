using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Rev.Core.Models;
using Rev.Linux.Views;

namespace Rev.Linux.Controls;

/// <summary>
/// A deck in Home's table: colour, name, then Due, New and Total. Click (or Enter) opens it, Space studies it,
/// F2 renames, Delete deletes; right-click shows the same choices.
/// </summary>
public sealed class DeckListRow : Button
{
    /// <summary>The table's columns, shared with the heading row so they line up.</summary>
    public const string Columns = "*,80,80,80";

    public Deck Deck { get; }

    protected override Type StyleKeyOverride => typeof(Button);

    public DeckListRow(Deck deck, int index, int dueCount, int newCount, int totalCount)
    {
        Deck = deck;
        Classes.Add("rev");
        Classes.Add("tablerow");
        this.Named(deck.Name);

        var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }
            .Colour(Shape.FillProperty, Ui.DeckColours(index).Background);
        // A grid (not a StackPanel) so the name is limited to its column and ends in "…" when long.
        var name = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 12, 0) };
        name.Children.Add(dot);
        var nameText = new TextBlock { Text = deck.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(nameText, 1);
        name.Children.Add(nameText);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns) };
        grid.Children.Add(name);
        AddNumber(grid, 1, dueCount, dueCount > 0 ? "TealBrush" : "MutedInkBrush");
        AddNumber(grid, 2, newCount, newCount > 0 ? "CoralBrush" : "MutedInkBrush");
        AddNumber(grid, 3, totalCount, "MutedInkBrush");
        Content = grid;

        var study = new MenuItem { Header = "Study", InputGesture = new KeyGesture(Key.Space) };
        study.Click += (_, _) => Study();
        var rename = new MenuItem { Header = "Rename", InputGesture = new KeyGesture(Key.F2) };
        rename.Click += (_, _) => _ = DeckActions.Rename(this, Deck);
        var delete = new MenuItem { Header = "Delete", InputGesture = new KeyGesture(Key.Delete) };
        delete.Click += (_, _) => _ = DeckActions.Delete(this, Deck);
        ContextMenu = new ContextMenu { Items = { study, rename, new Separator(), delete } };

        Click += (_, _) => MainWindow.Current.ShowDeck(Deck);
        // Tunnel, so Space studies instead of clicking (which would open the deck).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Space) Study();
            else if (e.Key == Key.F2) _ = DeckActions.Rename(this, Deck);
            else if (e.Key == Key.Delete) _ = DeckActions.Delete(this, Deck);
            else return;
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void Study() => MainWindow.Current.Open(new ReviewView(Deck));

    private static void AddNumber(Grid grid, int column, int value, string colour)
    {
        var text = new TextBlock { Text = value.ToString(), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        text.Colour(TextBlock.ForegroundProperty, colour);
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }
}
