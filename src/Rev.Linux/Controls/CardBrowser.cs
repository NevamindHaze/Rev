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
/// A searchable, filterable, sortable list of cards, used inside a deck and on the "All cards" screen.
/// Click a card to edit it; right-click it (or its ⋯ button) for tags, suspend, leech and delete.
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
    private readonly TextBlock countText = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 4, 0, 10) };
    private readonly StackPanel cardList = new();
    private readonly Button moreButton = Ui.Button("rev secondary", "");

    /// <summary>Waits a moment after you stop typing before searching, so typing stays smooth.</summary>
    private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public event EventHandler? Changed;

    public CardBrowser()
    {
        searchBox.Classes.Add("rev");
        searchBox.Named("Search cards");
        countText.Classes.Add("muted");
        moreButton.MinHeight = 48;
        moreButton.HorizontalAlignment = HorizontalAlignment.Center;
        moreButton.Margin = new Thickness(0, 6, 0, 0);
        moreButton.IsVisible = false;
        ToolTip.SetTip(stateChip, "Show only some cards");
        ToolTip.SetTip(tagChip, "Show only cards with a tag");
        ToolTip.SetTip(sortChip, "Change the order");

        Children.Add(searchBox);
        Children.Add(new WrapPanel { Margin = new Thickness(0, 12, 0, 0), Children = { stateChip, tagChip, sortChip } });
        Children.Add(countText);
        Children.Add(cardList);
        Children.Add(moreButton);

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
            badges.Children.Add(new TextBlock
            {
                Text = deckName, FontSize = 12, FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0),
            }.Colour(TextBlock.ForegroundProperty, "MutedInkBrush"));

        var text = new StackPanel { Children = { badges, Lines(card.Front, 17, FontWeight.SemiBold, null) } };
        if (card.Back.Length > 0) text.Children.Add(Lines(card.Back, 15, FontWeight.Normal, "MutedInkBrush"));
        var tags = Tags.Parse(card.TagsText);
        if (tags.Count > 0)
            text.Children.Add(new TextBlock
            {
                Text = string.Join("  ", tags.Select(t => "#" + t)), FontSize = 13, FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            }.Colour(TextBlock.ForegroundProperty, "TealBrush"));

        var more = Ui.Button("rev secondary round", Ui.Icon(MaterialIconKind.DotsHorizontal, 18)).Named("Actions");
        more.Width = more.Height = 38;
        more.VerticalAlignment = VerticalAlignment.Top;
        more.Margin = new Thickness(12, 0, 0, 0);
        more.Focusable = false;
        ToolTip.SetTip(more, "More actions");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        Grid.SetColumn(more, 1);
        grid.Children.Add(more);

        var row = Ui.Button("rev cardrow", grid).Named(CardText.Preview(card.Front));
        row.Opacity = card.IsSuspended ? 0.6 : 1;
        row.Tag = card.Id;
        var menu = new ContextMenu();
        menu.Opening += (_, _) => FillMenu(menu, card);
        row.ContextMenu = menu;
        row.Click += (_, _) => _ = Edit(card);
        row.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete) return;
            e.Handled = true;
            _ = Delete(card);
        };
        more.Click += (_, e) =>
        {
            e.Handled = true;
            menu.Open(more);
        };
        return row;
    }

    private static Border Badge(string label, string background, string foreground) => new Border
    {
        Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(11),
        Child = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.Bold }.Colour(TextBlock.ForegroundProperty, foreground),
    }.Colour(Border.BackgroundProperty, background);

    /// <summary>Up to two lines of a card's text, with bold and italic shown.</summary>
    private static TextBlock Lines(string value, double size, FontWeight weight, string? colour)
    {
        var block = new TextBlock
        {
            FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2, Margin = new Thickness(0, 1, 0, 1),
        };
        if (colour != null) block.Colour(TextBlock.ForegroundProperty, colour);
        RichText.Fill(block, CardText.Preview(value));
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
