using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.Linux.Controls;

/// <summary>
/// One deck on the Home screen: a big coloured card with its name and "N cards · N due · N new" chips.
/// Click (or Enter) opens it, F2 renames, Delete deletes; right-click shows the same choices.
/// The screen listens to its three events.
/// </summary>
public sealed class DeckTile : Button
{
    /// <summary>The tile colours take turns down the list: coral → teal → mustard → sage.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    public Deck Deck { get; }

    public event EventHandler? OpenRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? DeleteRequested;

    public DeckTile(Deck deck, int index, int cardCount, int dueCount, int newCount)
    {
        Deck = deck;
        Classes.Add("rev");
        Classes.Add("tile");
        var (background, foreground) = Ui.DeckColours(index);
        this.Colour(BackgroundProperty, background);
        this.Colour(ForegroundProperty, foreground);
        this.Named(deck.Name);
        ToolTip.SetTip(this, "Right-click for Rename and Delete");

        var name = new TextBlock { Text = deck.Name, FontSize = 24, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };

        // The little arrow in a circle
        var arrow = new Panel
        {
            Width = 42, Height = 42, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new Ellipse { Opacity = 0.15 }.Colour(Shape.FillProperty, foreground),
                Ui.Icon(MaterialIconKind.ArrowTopRight, 18).Center(),
            },
        };

        var chips = new WrapPanel { Margin = new Thickness(0, 18, 0, -6) };
        chips.Children.Add(Chip(cardCount.Counted("card"), foreground));
        chips.Children.Add(Chip($"{dueCount} due", foreground));
        chips.Children.Add(Chip($"{newCount} new", foreground));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        grid.Children.Add(name);
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);
        Grid.SetRow(chips, 1);
        Grid.SetColumnSpan(chips, 2);
        grid.Children.Add(chips);
        Content = grid;

        var rename = new MenuItem { Header = "Rename", InputGesture = new KeyGesture(Key.F2) };
        rename.Click += (_, _) => RenameRequested?.Invoke(this, EventArgs.Empty);
        var delete = new MenuItem { Header = "Delete", InputGesture = new KeyGesture(Key.Delete) };
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        ContextMenu = new ContextMenu { Items = { rename, delete } };

        Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F2) RenameRequested?.Invoke(this, EventArgs.Empty);
            else if (e.Key == Key.Delete) DeleteRequested?.Invoke(this, EventArgs.Empty);
            else return;
            e.Handled = true;
        };
    }

    /// <summary>A small rounded label. Its see-through background takes the tile's text colour.</summary>
    private static Panel Chip(string text, string foreground) => new()
    {
        Margin = new Thickness(0, 0, 8, 6),
        Children =
        {
            new Border { Opacity = 0.16, CornerRadius = new CornerRadius(999) }.Colour(Border.BackgroundProperty, foreground),
            new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 5) },
        },
    };
}

public static class LayoutExtensions
{
    /// <summary>Centres a control in the space it's given.</summary>
    public static T Center<T>(this T control) where T : Layoutable
    {
        control.HorizontalAlignment = HorizontalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }
}
