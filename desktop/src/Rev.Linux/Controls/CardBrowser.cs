using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Rev.Core.Browser;
using Rev.Core.Models;
using Rev.Core.Text;
using Rev.Linux.Views;

namespace Rev.Linux.Controls;

/// <summary>
/// A searchable, filterable, sortable table of cards, used inside a deck and on the "All cards" page.
/// Columns: Front, Back, Type, Due (and Deck on "All cards"); click Front or Due to sort by it.
/// Double-click a card (or Enter) to edit it, Delete deletes, ↑ / ↓ move; right-click for tags, suspend and more.
/// The screen that holds it calls <see cref="Setup"/> once (saying where the cards come from) and
/// <see cref="Refresh"/> when it comes back. <see cref="Changed"/> fires after any edit.
/// </summary>
public sealed class CardBrowser : StackPanel
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

    private readonly TextBox searchBox = new() { Watermark = "Search cards or tags (Ctrl+F)" };
    private readonly Button stateChip = Ui.Button("rev secondary chip", "");
    private readonly Button tagChip = Ui.Button("rev secondary chip", "");
    private readonly Button sortChip = Ui.Button("rev secondary chip", "");
    private readonly TextBlock countText = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 10, 0, 6) };
    private readonly Grid heading = new() { Margin = new Thickness(10, 2, 10, 4) };
    private readonly StackPanel cardList = new();
    private readonly Button moreButton = Ui.Button("rev secondary", "");
    private readonly Button frontHeading = SortHeading("Front");
    private readonly Button dueHeading = SortHeading("Due");

    /// <summary>Waits a moment after you stop typing before searching, so typing stays smooth.</summary>
    private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public event EventHandler? Changed;

    public CardBrowser()
    {
        searchBox.Classes.Add("rev");
        searchBox.Named("Search cards");
        countText.Classes.Add("muted");
        moreButton.HorizontalAlignment = HorizontalAlignment.Center;
        moreButton.Margin = new Thickness(0, 6, 0, 0);
        moreButton.IsVisible = false;
        ToolTip.SetTip(stateChip, "Show only some cards");
        ToolTip.SetTip(tagChip, "Show only cards with a tag");
        ToolTip.SetTip(sortChip, "Change the order");

        // The search box and the three menus on one row, then the count, then the table.
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        tools.Children.Add(searchBox);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, -6, 0), Children = { stateChip, tagChip, sortChip } };
        Grid.SetColumn(chips, 1);
        tools.Children.Add(chips);
        Children.Add(tools);
        Children.Add(countText);
        Children.Add(new Border { Classes = { "table" }, Child = new StackPanel { Children = { heading, cardList, moreButton } } });

        frontHeading.Click += (_, _) => SortBy(CardSort.AToZ);
        dueHeading.Click += (_, _) => SortBy(CardSort.DueSoonest);

        searchDelay.Tick += (_, _) =>
        {
            searchDelay.Stop();
            shown = PageSize;
            Render();
        };
        searchBox.TextChanged += (_, _) =>
        {
            searchDelay.Stop();
            searchDelay.Start();
        };
        // Esc in the search box clears it first (a second Esc then goes back).
        searchBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || string.IsNullOrEmpty(searchBox.Text)) return;
            searchBox.Text = "";
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        stateChip.Click += (_, _) =>
            ShowChoices(stateChip, Enum.GetValues<CardStateFilter>().Select(f => (f.Title(), f == state, (Action)(() => state = f))));
        tagChip.Click += (_, _) =>
            ShowChoices(tagChip, CardQuery.AllTags(cards).Select(t => ("#" + t, t == tag, (Action)(() => tag = t)))
                .Prepend(("Any tag", tag is null, () => tag = null)));
        sortChip.Click += (_, _) =>
            ShowChoices(sortChip, Enum.GetValues<CardSort>().Select(s => (s.Title(), s == sort, (Action)(() => sort = s))));
        moreButton.Click += (_, _) =>
        {
            shown += PageSize;
            Render();
        };
    }

    /// <param name="load">Where the cards come from (one deck's, or all of them).</param>
    /// <param name="withDeckNames">Show each card's deck (for "All cards").</param>
    public void Setup(Func<List<Card>> load, bool withDeckNames)
    {
        loadCards = load;
        showDeckNames = withDeckNames;
        BuildHeading();
        Refresh();
    }

    /// <summary>Reads the cards again and redraws the list, keeping your search, filters and sort.</summary>
    public void Refresh()
    {
        // Redrawing replaces the rows; remember which card had the keyboard, to give it back after.
        var focused = Rows.FirstOrDefault(r => r.IsKeyboardFocusWithin)?.Tag;
        cards = loadCards();
        deckNames = App.Database.GetDecks().ToDictionary(d => d.Id, d => d.Name);
        Render();
        if (focused != null) Rows.FirstOrDefault(r => focused.Equals(r.Tag))?.Focus(NavigationMethod.Tab);
    }

    public void FocusSearch()
    {
        searchBox.Focus();
        searchBox.SelectAll();
    }

    private IEnumerable<Button> Rows => cardList.Children.OfType<Button>();

    // ---- Drawing ----

    private void Render()
    {
        var now = DateTime.Now;
        var visible = CardQuery.Apply(cards, searchBox.Text ?? "", state, tag, sort, now);

        SetChip(stateChip, MaterialIconKind.FilterVariant, state.Title(), state != CardStateFilter.All, "Show");
        SetChip(tagChip, MaterialIconKind.TagOutline, tag is null ? "Any tag" : "#" + tag, tag is not null, "Tag");
        SetChip(sortChip, MaterialIconKind.SortVariant, sort.Title(), false, "Sort");
        frontHeading.Classes.Set("selected", sort == CardSort.AToZ);
        dueHeading.Classes.Set("selected", sort == CardSort.DueSoonest);

        countText.Text = visible.Count == cards.Count
            ? visible.Count.Counted("card")
            : $"{visible.Count.Counted("card")} of {cards.Count}";

        cardList.Children.Clear();
        foreach (var card in visible.Take(shown)) cardList.Children.Add(Row(card, now));

        int remaining = visible.Count - shown;
        moreButton.IsVisible = remaining > 0;
        moreButton.Content = $"Show {Math.Min(PageSize, remaining)} more";
    }

    /// <summary>A filter chip: teal when it's narrowing the list.</summary>
    private static void SetChip(Button chip, MaterialIconKind icon, string text, bool active, string what)
    {
        chip.Classes.Set("secondary", !active);
        chip.Classes.Set("teal", active);
        chip.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Ui.Icon(icon, 15).WithMargin(new Thickness(0, 0, 8, 0)),
                new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                Ui.Icon(MaterialIconKind.ChevronDown, 14).WithMargin(new Thickness(6, 1, 0, 0)),
            },
        };
        chip.Named($"{what}: {text}");
    }

    // ---- The table ----

    /// <summary>The table's columns: Deck (on "All cards"), Front, Back, Type, Due.</summary>
    private string Columns => showDeckNames ? "140,2*,1.5*,130,110" : "2*,1.5*,130,110";

    private void BuildHeading()
    {
        heading.ColumnDefinitions = new ColumnDefinitions(Columns);
        heading.Children.Clear();
        int column = 0;
        if (showDeckNames) AddCell(heading, column++, new TextBlock { Classes = { "columnhead" }, Text = "Deck" });
        AddCell(heading, column++, frontHeading);
        AddCell(heading, column++, new TextBlock { Classes = { "columnhead" }, Text = "Back" });
        AddCell(heading, column++, new TextBlock { Classes = { "columnhead" }, Text = "Type" });
        AddCell(heading, column, dueHeading);
    }

    /// <summary>A column heading you can click to sort by that column.</summary>
    private static Button SortHeading(string title)
    {
        var button = Ui.Button("rev toolbar columnsort", title).Named(title);
        ToolTip.SetTip(button, $"Sort by {title.ToLowerInvariant()}");
        return button;
    }

    private void SortBy(CardSort newSort)
    {
        sort = newSort;
        shown = PageSize;
        Render();
    }

    private static void AddCell(Grid grid, int column, Control cell)
    {
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    /// <summary>One card as a table row. Its tags (and the whole question) show when you point at it.</summary>
    private Button Row(Card card, DateTime now)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns) };
        int column = 0;
        if (showDeckNames)
            AddCell(grid, column++, Cell(deckNames.GetValueOrDefault(card.DeckId, ""), "MutedInkBrush"));
        AddCell(grid, column++, Cell(card.Front, null, FontWeight.SemiBold, rich: true));
        AddCell(grid, column++, Cell(card.Back, "MutedInkBrush", rich: true));
        string type = DeckView.KindName(card.Kind)
                      + (card.IsLeech ? " · Leech" : "") + (card.IsSuspended ? " · Suspended" : "");
        AddCell(grid, column++, Cell(type, card.IsLeech ? "CoralBrush" : "MutedInkBrush"));
        string dueColour = card.Phase switch
        {
            Rev.Core.Scheduling.CardPhase.New => "CoralBrush",
            Rev.Core.Scheduling.CardPhase.Review => "TealBrush",
            _ => "MustardBrush",
        };
        AddCell(grid, column, Cell(CardQuery.StateText(card, now), dueColour));

        var row = Ui.Button("rev tablerow", grid).Named(CardText.Preview(card.Front));
        row.Opacity = card.IsSuspended ? 0.6 : 1;
        row.Tag = card.Id;
        var tags = Tags.Parse(card.TagsText);
        ToolTip.SetTip(row, CardText.Preview(card.Front).Replace("*", "")
                            + (tags.Count > 0 ? "\n" + string.Join("  ", tags.Select(t => "#" + t)) : ""));
        var menu = new ContextMenu();
        menu.Opening += (_, _) => FillMenu(menu, card);
        row.ContextMenu = menu;
        row.DoubleTapped += (_, _) => _ = Edit(card);
        // Tunnel, so Enter edits instead of "clicking" the row.
        row.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter: _ = Edit(card); break;
                case Key.Delete: _ = Delete(card); break;
                case Key.Down: MoveFocus(row, +1); break;
                case Key.Up: MoveFocus(row, -1); break;
                default: return;
            }
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        return row;
    }

    private void MoveFocus(Button from, int step)
    {
        var rows = Rows.ToList();
        int next = rows.IndexOf(from) + step;
        if (next >= 0 && next < rows.Count) rows[next].Focus(NavigationMethod.Directional);
    }

    /// <summary>One line of text ending in "…" when it doesn't fit; card text shows its bold and italic.</summary>
    private static TextBlock Cell(string value, string? colour, FontWeight? weight = null, bool rich = false)
    {
        var block = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
        };
        if (weight is { } w) block.FontWeight = w;
        if (colour != null) block.Colour(TextBlock.ForegroundProperty, colour);
        if (rich) RichText.Fill(block, CardText.Preview(value));
        else block.Text = value;
        return block;
    }

    // ---- The actions menu ----

    private void FillMenu(ContextMenu menu, Card card)
    {
        menu.Items.Clear();
        Add(menu, "Edit", () => _ = Edit(card), Key.Enter);
        Add(menu, "Add tags…", () => _ = AddTags(card));
        foreach (var t in Tags.Parse(card.TagsText))
            Add(menu, $"Remove tag “{t}”", () => Change(() => App.Database.RemoveTag(card.Id, t), card.Id));
        Add(menu, card.IsSuspended ? "Unsuspend" : "Suspend",
            () => Change(() => App.Database.SetSuspended(card.Id, !card.IsSuspended), card.Id));
        if (card.IsLeech) Add(menu, "Not a leech any more", () => Change(() => App.Database.ClearLeech(card.Id), card.Id));
        Add(menu, "Delete…", () => _ = Delete(card), Key.Delete);
    }

    private static void Add(ContextMenu menu, string header, Action action, Key? gesture = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture is { } key) item.InputGesture = new KeyGesture(key);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private async Task Edit(Card card)
    {
        var deck = App.Database.GetDecks().FirstOrDefault(d => d.Id == card.DeckId);
        if (deck is null) return;
        await new CardEditorWindow(deck, card).ShowDialog(MainWindow.Current);
        Change(() => { }, card.Id);
    }

    private async Task AddTags(Card card)
    {
        var text = await RevDialog.Prompt(this, "Add tags", "Separate tags with spaces.", "", "Add", "e.g. high_yield");
        if (text is null) return;
        Change(() => App.Database.AddTags(card.Id, Tags.Parse(text)), card.Id);
    }

    private async Task Delete(Card card)
    {
        int siblings = App.Database.CountSiblings(card);
        string message = "This permanently deletes the card and its study progress. This can't be undone.";
        if (siblings > 0) message += $" The other {siblings.Counted("card")} made from the same note will stay.";
        if (!await RevDialog.Confirm(this, "Delete this card?", message, "Delete", destructive: true)) return;
        Change(() => App.Database.DeleteCard(card.Id), card.Id);
    }

    /// <summary>
    /// Does a change, then redraws and tells the screen. The keyboard goes back to the same card's row
    /// (or, if it was deleted, the row now in its place), so shortcuts keep working without a click.
    /// </summary>
    private void Change(Action action, Guid? cardId = null)
    {
        var rows = Rows.ToList();
        var focused = cardId ?? rows.FirstOrDefault(r => r.IsKeyboardFocusWithin)?.Tag as Guid?;
        int place = rows.FindIndex(r => focused.Equals(r.Tag));

        action();
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);

        if (place < 0) return;
        var newRows = Rows.ToList();
        var target = newRows.FirstOrDefault(r => focused.Equals(r.Tag))
                     ?? (newRows.Count > 0 ? newRows[Math.Min(place, newRows.Count - 1)] : null);
        if (target != null) target.Focus(NavigationMethod.Tab);
        else searchBox.Focus();
    }

    // ---- Filters ----

    /// <summary>A drop-down list under a chip; the current choice has a tick.</summary>
    private void ShowChoices(Button chip, IEnumerable<(string Title, bool Current, Action Choose)> choices)
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };
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
        menu.Open(chip);
    }
}

public static class ControlExtensions
{
    public static T WithMargin<T>(this T control, Thickness margin) where T : Layoutable
    {
        control.Margin = margin;
        return control;
    }
}
