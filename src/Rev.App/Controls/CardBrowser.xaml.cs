using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Rev.App.Views;
using Rev.Core.Browser;
using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.App.Controls;

/// <summary>
/// The card browser. The screen that holds it calls <see cref="Setup"/> once (saying where the cards
/// come from) and <see cref="Refresh"/> when it comes back. <see cref="Changed"/> fires after any edit,
/// so the screen can update its numbers.
/// </summary>
public partial class CardBrowser : UserControl
{
    /// <summary>Cards are shown 100 at a time, so a big Anki deck stays quick.</summary>
    private const int PageSize = 100;

    private Func<List<Card>> loadCards = () => [];
    private bool showDeckNames;
    private List<Card> cards = [];
    private Dictionary<Guid, string> deckNames = [];

    private CardStateFilter state = CardStateFilter.All;
    private string? tag;
    private CardSort sort = CardSort.Newest;
    private int shown = PageSize;

    /// <summary>Waits a moment after you stop typing before searching, so typing stays smooth.</summary>
    private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public event EventHandler? Changed;

    public CardBrowser()
    {
        InitializeComponent();
        searchDelay.Tick += (_, _) =>
        {
            searchDelay.Stop();
            shown = PageSize;
            Render();
        };
    }

    /// <param name="load">Where the cards come from (one deck's, or all of them).</param>
    /// <param name="withDeckNames">Show each card's deck (for "All cards").</param>
    public void Setup(Func<List<Card>> load, bool withDeckNames)
    {
        loadCards = load;
        showDeckNames = withDeckNames;
        Refresh();
    }

    /// <summary>Reads the cards again and redraws the list, keeping your search, filters and sort.</summary>
    public void Refresh()
    {
        cards = loadCards();
        deckNames = App.Database.GetDecks().ToDictionary(d => d.Id, d => d.Name);
        Render();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ---- Drawing ----

    private void Render()
    {
        var now = DateTime.Now;
        var visible = CardQuery.Apply(cards, SearchBox.Text, state, tag, sort, now);

        SetChip(StateChip, "", state.Title(), state != CardStateFilter.All, "Show");
        SetChip(TagChip, "", tag is null ? "Any tag" : "#" + tag, tag is not null, "Tag");
        SetChip(SortChip, "", sort.Title(), false, "Sort");

        CountText.Text = visible.Count == cards.Count
            ? visible.Count.Counted("card")
            : $"{visible.Count.Counted("card")} of {cards.Count}";

        CardList.Children.Clear();
        foreach (var card in visible.Take(shown)) CardList.Children.Add(Row(card, now));

        int remaining = visible.Count - shown;
        MoreButton.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Content = $"Show {Math.Min(PageSize, remaining)} more";
    }

    private void SetChip(Button chip, string glyph, string text, bool active, string what)
    {
        chip.Style = (Style)FindResource(active ? "ActiveChipButton" : "ChipButton");
        chip.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Style = (Style)FindResource("IconText"), Text = glyph, FontSize = 13, Margin = new Thickness(0, 0, 8, 0) },
                new TextBlock { Text = text },
                new TextBlock { Style = (Style)FindResource("IconText"), Text = "", FontSize = 10, Margin = new Thickness(8, 1, 0, 0) },
            },
        };
        AutomationProperties.SetName(chip, $"{what}: {text}");
    }

    /// <summary>One card: badges, question, answer, tags, and a ⋯ button for its actions.</summary>
    private Button Row(Card card, DateTime now)
    {
        var badges = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var (stateBg, stateFg) = card.Phase switch
        {
            Rev.Core.Scheduling.CardPhase.New => ("CoralBrush", "OnCoralBrush"),
            Rev.Core.Scheduling.CardPhase.Review => ("TealBrush", "OnTealBrush"),
            _ => ("MustardBrush", "OnMustardBrush"),
        };
        badges.Children.Add(Badge(CardQuery.StateText(card, now), stateBg, stateFg));
        if (card.Kind != CardKind.Basic) badges.Children.Add(Badge(DeckView.KindName(card.Kind), "MustardBrush", "OnMustardBrush"));
        if (card.IsLeech) badges.Children.Add(Badge("Leech", "CoralBrush", "OnCoralBrush"));
        if (card.IsSuspended) badges.Children.Add(Badge("Suspended", "MutedInkBrush", "SurfaceBrush"));
        if (showDeckNames && deckNames.TryGetValue(card.DeckId, out var deckName))
        {
            var deckText = new TextBlock { Text = deckName, FontSize = 12, FontWeight = FontWeights.SemiBold,
                                           VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            deckText.SetResourceReference(TextBlock.ForegroundProperty, "MutedInkBrush");
            badges.Children.Add(deckText);
        }

        var text = new StackPanel { Children = { badges, Lines(card.Front, 17, FontWeights.SemiBold, null) } };
        if (card.Back.Length > 0) text.Children.Add(Lines(card.Back, 15, FontWeights.Normal, "MutedInkBrush"));
        var tags = Tags.Parse(card.TagsText);
        if (tags.Count > 0)
        {
            var tagText = new TextBlock { Text = string.Join("  ", tags.Select(t => "#" + t)), FontSize = 13,
                                          FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0),
                                          TextTrimming = TextTrimming.CharacterEllipsis };
            tagText.SetResourceReference(TextBlock.ForegroundProperty, "TealBrush");
            text.Children.Add(tagText);
        }

        var more = new Button
        {
            Style = (Style)FindResource("RoundIconButton"), Width = 38, Height = 38, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 0, 0, 0), Content = new TextBlock { Style = (Style)FindResource("IconText"), Text = "" },
            ToolTip = "More actions", Focusable = false,
        };
        AutomationProperties.SetName(more, "Actions");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(more, 1);
        grid.Children.Add(more);

        var row = new Button
        {
            Style = (Style)FindResource("CardRowButton"), Content = grid, Opacity = card.IsSuspended ? 0.6 : 1, Tag = card.Id,
        };
        AutomationProperties.SetName(row, CardText.Preview(card.Front));
        row.ContextMenu = new ContextMenu();
        row.ContextMenuOpening += (_, _) => FillMenu(row.ContextMenu, card);
        row.Click += (_, _) => Edit(card);
        row.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete) return;
            e.Handled = true;
            Delete(card);
        };
        more.Click += (_, e) =>
        {
            e.Handled = true;
            FillMenu(row.ContextMenu, card);
            row.ContextMenu.PlacementTarget = more;
            row.ContextMenu.IsOpen = true;
        };
        return row;
    }

    private Border Badge(string label, string background, string foreground)
    {
        var text = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Bold };
        text.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        var badge = new Border { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(11), Child = text };
        badge.SetResourceReference(Border.BackgroundProperty, background);
        return badge;
    }

    /// <summary>Up to two lines of a card's text, with bold and italic shown.</summary>
    private static TextBlock Lines(string value, double size, FontWeight weight, string? colour)
    {
        var block = new TextBlock
        {
            FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = size * 1.42 * 2, Margin = new Thickness(0, 1, 0, 1),
        };
        if (colour != null) block.SetResourceReference(TextBlock.ForegroundProperty, colour);
        RichText.Fill(block, CardText.Preview(value));
        return block;
    }

    // ---- The actions menu ----

    private void FillMenu(ContextMenu menu, Card card)
    {
        menu.Items.Clear();
        Add(menu, "Edit", () => Edit(card), "Enter");
        Add(menu, "Add tags…", () => AddTags(card));
        foreach (var t in Tags.Parse(card.TagsText))
            Add(menu, $"Remove tag “{t}”", () => Change(() => App.Database.RemoveTag(card.Id, t), card.Id));
        Add(menu, card.IsSuspended ? "Unsuspend" : "Suspend",
            () => Change(() => App.Database.SetSuspended(card.Id, !card.IsSuspended), card.Id));
        if (card.IsLeech) Add(menu, "Not a leech any more", () => Change(() => App.Database.ClearLeech(card.Id), card.Id));
        Add(menu, "Delete…", () => Delete(card), "Del");
    }

    private static void Add(ContextMenu menu, string header, Action action, string gesture = "")
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private Window? OwnerWindow => Window.GetWindow(this);

    private void Edit(Card card)
    {
        var deck = App.Database.GetDecks().FirstOrDefault(d => d.Id == card.DeckId);
        if (deck is null) return;
        new CardEditorWindow(deck, card) { Owner = OwnerWindow }.ShowDialog();
        Change(() => { }, card.Id);
    }

    private void AddTags(Card card)
    {
        var text = RevDialog.Prompt(OwnerWindow, "Add tags", "Separate tags with spaces.", "", "Add", "e.g. high_yield");
        if (text is null) return;
        Change(() => App.Database.AddTags(card.Id, Tags.Parse(text)), card.Id);
    }

    private void Delete(Card card)
    {
        int siblings = App.Database.CountSiblings(card);
        string message = "This permanently deletes the card and its study progress. This can't be undone.";
        if (siblings > 0) message += $" The other {siblings.Counted("card")} made from the same note will stay.";
        if (!RevDialog.Confirm(OwnerWindow, "Delete this card?", message, "Delete", destructive: true)) return;
        Change(() => App.Database.DeleteCard(card.Id), card.Id);
    }

    /// <summary>
    /// Does a change, then redraws and tells the screen. The keyboard goes back to the same card's row
    /// (or, if it was deleted, the row now in its place), so shortcuts keep working without a click.
    /// </summary>
    private void Change(Action action, Guid? cardId = null)
    {
        var rows = CardList.Children.OfType<Button>().ToList();
        var focused = cardId ?? rows.FirstOrDefault(r => r.IsKeyboardFocusWithin)?.Tag as Guid?;
        int place = rows.FindIndex(r => focused.Equals(r.Tag));

        action();
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);

        if (place < 0) return;
        var newRows = CardList.Children.OfType<Button>().ToList();
        var target = newRows.FirstOrDefault(r => focused.Equals(r.Tag))
                     ?? (newRows.Count > 0 ? newRows[Math.Min(place, newRows.Count - 1)] : null);
        if (target != null) target.Focus();
        else SearchBox.Focus();
    }

    // ---- Filters ----

    private void StateChip_Click(object sender, RoutedEventArgs e) =>
        ShowChoices(StateChip, Enum.GetValues<CardStateFilter>().Select(f => (f.Title(), f == state, (Action)(() => state = f))));

    private void TagChip_Click(object sender, RoutedEventArgs e) =>
        ShowChoices(TagChip, CardQuery.AllTags(cards).Select(t => ("#" + t, t == tag, (Action)(() => tag = t)))
            .Prepend(("Any tag", tag is null, () => tag = null)));

    private void SortChip_Click(object sender, RoutedEventArgs e) =>
        ShowChoices(SortChip, Enum.GetValues<CardSort>().Select(s => (s.Title(), s == sort, (Action)(() => sort = s))));

    /// <summary>A drop-down list under a chip; the current choice has a tick.</summary>
    private void ShowChoices(Button chip, IEnumerable<(string Title, bool Current, Action Choose)> choices)
    {
        var menu = new ContextMenu { PlacementTarget = chip, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (title, current, choose) in choices)
        {
            var item = new MenuItem { Header = (current ? "✓  " : "     ") + title };
            item.Click += (_, _) =>
            {
                choose();
                shown = PageSize;
                Render();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ---- Search ----

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        searchDelay.Stop();
        searchDelay.Start();
    }

    /// <summary>Esc in the search box clears it first (a second Esc then goes back).</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        shown += PageSize;
        Render();
    }
}
