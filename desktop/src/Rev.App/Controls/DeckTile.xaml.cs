using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.App.Controls;

/// <summary>One deck on the Home screen. The screen listens to its three events.</summary>
public partial class DeckTile : UserControl
{
    /// <summary>The tile colours take turns down the list: coral → teal → mustard → sage.</summary>
    private static readonly (string Background, string Foreground)[] Palettes =
    [
        ("CoralBrush", "OnCoralBrush"),
        ("TealBrush", "OnTealBrush"),
        ("MustardBrush", "OnMustardBrush"),
        ("SageBrush", "OnSageBrush"),
    ];

    public Deck Deck { get; }

    public event EventHandler? OpenRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? DeleteRequested;

    public DeckTile(Deck deck, int index, int cardCount, int dueCount, int newCount)
    {
        InitializeComponent();
        Deck = deck;

        // SetResourceReference = "use this colour name", so the tile follows light/dark mode.
        var (background, foreground) = Palettes[index % Palettes.Length];
        TileButton.SetResourceReference(BackgroundProperty, background);
        TileButton.SetResourceReference(ForegroundProperty, foreground);

        NameText.Text = deck.Name;
        CardsChip.Text = cardCount.Counted("card");
        DueChip.Text = $"{dueCount} due";
        NewChip.Text = $"{newCount} new";

        // What screen readers (and automatic UI tests) call this tile.
        AutomationProperties.SetName(TileButton, deck.Name);
        ToolTipService.SetToolTip(TileButton, "Right-click for Rename and Delete");
    }

    /// <summary>Moves the keyboard to this tile.</summary>
    public void FocusTile() => TileButton.Focus();

    private void TileButton_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private void TileButton_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) RenameRequested?.Invoke(this, EventArgs.Empty);
        else if (e.Key == Key.Delete) DeleteRequested?.Invoke(this, EventArgs.Empty);
        else return;
        e.Handled = true;
    }

    private void Rename_Click(object sender, RoutedEventArgs e) => RenameRequested?.Invoke(this, EventArgs.Empty);

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
}
