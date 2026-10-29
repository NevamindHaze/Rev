using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Rev.Core.Models;
using Rev.Core.Text;
using Rev.Linux.Controls;

namespace Rev.Linux.Views;

/// <summary>
/// Adds new cards to a deck (choose the kind; "Save &amp; next" keeps the window open and keeps your tags),
/// or edits an existing card (pass it as <c>cardToEdit</c>).
/// </summary>
public partial class CardEditorWindow : Window
{
    private readonly Deck deck;
    private readonly Card? editing;
    private bool IsAdding => editing is null;

    private CardKind kind = CardKind.Basic;
    private List<Card> allCards;          // for tag suggestions
    private CardTextBox lastTextBox;          // where Bold / Italic apply
    private int addedCount;
    private bool closingChecked;          // so the "discard?" question is asked only once
    private bool ready;                   // false while the window is being filled in
    private (string Front, string Back, string Tags) original = ("", "", "");
    private string? clozeMessage;         // e.g. "“word” isn't in the text", until the text changes

    public CardEditorWindow() : this(new Deck(), null) { }   // (for the XAML previewer only)

    public CardEditorWindow(Deck deck, Card? cardToEdit)
    {
        InitializeComponent();
        this.deck = deck;
        editing = cardToEdit;
        allCards = App.Database?.GetCards() ?? [];
        lastTextBox = FrontBox;

        DeckText.Text = deck.Name;
        if (editing is { } card)
        {
            Title = HeaderText.Text = "Edit card";
            kind = card.Kind;
            KindChoices.IsVisible = false;
            KindLabel.IsVisible = true;
            KindLabel.Text = KindTitle(kind);
            bool cloze = card.Kind == CardKind.Cloze && card.ClozeSource != null;
            FrontBox.Text = cloze ? card.ClozeSource! : card.Front;
            BackBox.Text = cloze ? card.ClozeExtra ?? "" : card.Back;
            TagsBox.Text = card.TagsText;
            original = (FrontBox.Text, BackBox.Text, TagsBox.Text);
            SecondaryButton.Content = "Cancel";
            PrimaryButton.Content = "Save";
            PrimaryButton.Classes.Remove("teal");
        }
        else
        {
            // Start with the kind you used last time.
            kind = Enum.TryParse(App.Database?.GetPreference("lastCardKind"), out CardKind last) ? last : CardKind.Basic;
            KindChoices.Children.OfType<RadioButton>().First(r => (string?)r.Tag == kind.ToString()).IsChecked = true;
        }

        FrontBox.TextChanged += (_, _) => TextChanged_();
        BackBox.TextChanged += (_, _) => TextChanged_();
        TagsBox.TextChanged += (_, _) => UpdateTagChips();
        HideWordBox.TextChanged += (_, _) => { if (ready) UpdateState(); };
        HideWordBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Hide(Cloze.NextNumber(FrontBox.Text ?? ""));
            e.Handled = true;
        };
        FrontBox.TypingStarted += (_, _) => lastTextBox = FrontBox;
        BackBox.TypingStarted += (_, _) => lastTextBox = BackBox;

        UpdateForKind();
        UpdateTagChips();
        ready = true;
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        Closing += Window_Closing;
        Opened += (_, _) =>
        {
            FitOnScreen();
            Dispatcher.UIThread.Post(() => FrontBox.Focus(), DispatcherPriority.Input);
        };
    }

    private string Front => FrontBox.Text;
    private string Back => BackBox.Text;
    private string TagsText => TagsBox.Text ?? "";

    // ---- What the window shows ----

    public static string KindTitle(CardKind kind) => kind switch
    {
        CardKind.Reversed => "Basic + reversed",
        CardKind.Cloze => "Cloze (fill in the blank)",
        CardKind.TypeIn => "Type the answer",
        _ => "Basic",
    };

    private static string Explanation(CardKind kind) => kind switch
    {
        CardKind.Reversed => "Two cards: one asks front → back, the other asks back → front. Great for drug ↔ class or term ↔ definition.",
        CardKind.Cloze => "Hide words inside a sentence. Type the word in “Word to hide” and click Hide, or write {{c1::word}} yourself. " +
                          "Each number (c1, c2…) becomes its own card; use the same number to hide several words together.",
        CardKind.TypeIn => "You type your answer before revealing, then see if it matched. Good for spellings, doses and normal values.",
        _ => "One card: question on the front, answer on the back.",
    };

    private int ClozeCount => Cloze.Numbers(Front).Count;

    private bool CanSave => kind == CardKind.Cloze ? ClozeCount > 0 : Front.Trim().Length > 0;

    private void UpdateForKind()
    {
        bool cloze = kind == CardKind.Cloze;
        KindExplanation.Text = Explanation(kind);
        FrontLabel.Text = cloze ? "Text" : "Front";
        BackLabel.Text = cloze ? "Extra (optional, shown on the back)" : "Back";
        ClozeTools.IsVisible = cloze;
        UpdateState();
    }

    /// <summary>Button titles, the cloze count and whether saving is possible, after any change.</summary>
    private void UpdateState()
    {
        int count = ClozeCount;
        int next = Cloze.NextNumber(Front);
        bool hasWord = (HideWordBox.Text ?? "").Trim().Length > 0;
        HideButton.Content = $"Hide c{next}";
        HideButton.IsEnabled = hasWord;
        AddToButton.Content = $"Add to c{next - 1}";
        AddToButton.IsVisible = next > 1;
        AddToButton.IsEnabled = hasWord;
        if (clozeMessage is null)
        {
            ClozeCountText.Text = count == 0 ? "Nothing hidden yet." : $"This makes {count.Counted("card")}.";
            ClozeCountText.Colour(TextBlock.ForegroundProperty, count == 0 ? "CoralBrush" : "TealBrush");
        }

        PrimaryButton.IsEnabled = CanSave;
        if (IsAdding)
        {
            PrimaryButton.Content = kind switch
            {
                CardKind.Reversed => "Save 2 cards & next",
                CardKind.Cloze when count > 1 => $"Save {count} cards & next",
                _ => "Save & next",
            };
            AddedText.Text = addedCount > 0 ? $"{addedCount.Counted("card")} added so far" : "";
        }
    }

    private void UpdateTagChips()
    {
        TagChips.Children.Clear();
        foreach (var tag in Tags.Suggestions(allCards, TagsText))
        {
            var chip = Ui.Button("rev secondary", "+ " + tag);
            chip.MinHeight = 36;
            chip.FontSize = 14;
            chip.Padding = new Thickness(14, 0, 14, 0);
            chip.Margin = new Thickness(0, 0, 8, 8);
            ToolTip.SetTip(chip, $"Add the tag “{tag}”");
            chip.Click += (_, _) =>
            {
                TagsBox.Text = Tags.Format(Tags.Parse(TagsText).Append(tag));
                TagsBox.CaretIndex = TagsText.Length;
            };
            TagChips.Children.Add(chip);
        }
    }

    // ---- Typing ----

    private void Kind_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true, Tag: string name }) return;
        kind = Enum.Parse<CardKind>(name);
        if (ready) UpdateForKind();
    }

    private void TextChanged_()
    {
        if (!ready) return;
        clozeMessage = null;
        UpdateState();
    }

    // ---- Bold and italic ----

    private void Bold_Click(object? sender, RoutedEventArgs e) => ApplyFormat("**");

    private void Italic_Click(object? sender, RoutedEventArgs e) => ApplyFormat("*");

    /// <summary>Wraps the selected words in ** (bold) or * (italic), or removes them if already there.</summary>
    private void ApplyFormat(string marker)
    {
        var box = lastTextBox;
        var (text, newStart, newLength) = TextTools.ToggleFormat(box.Text, marker, box.SelectionStart, box.SelectionLength);
        box.ReplaceText(text, newStart, newLength);
        box.Focus();
    }

    // ---- Pictures ----

    private async void Image_Click(object? sender, RoutedEventArgs e)
    {
        var box = lastTextBox;
        var path = await FileDialogs.OpenFile(this, "Add a picture", "Pictures",
            "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp");
        if (path is null) return;
        lastTextBox = box;
        AddImageFile(path);
    }

    /// <summary>Copies a picture into Rev and puts it in the box you were typing in, at the cursor.</summary>
    public void AddImageFile(string path)
    {
        var box = lastTextBox;
        if (CardImages.AddFile(path) is not { } name)
        {
            _ = RevDialog.Tell(this, "That isn't a picture", "Rev can add .png, .jpg, .gif, .webp and .bmp pictures.");
            return;
        }
        box.InsertImage(name);
        box.Focus();
    }

    // ---- Cloze helper ----

    private void Hide_Click(object? sender, RoutedEventArgs e) => Hide(Cloze.NextNumber(Front));

    private void AddTo_Click(object? sender, RoutedEventArgs e) => Hide(Cloze.NextNumber(Front) - 1);

    /// <summary>Wraps the first not-yet-hidden appearance of the word in {{cN::…}}.</summary>
    private void Hide(int number)
    {
        string word = (HideWordBox.Text ?? "").Trim();
        if (word.Length == 0) return;
        if (Cloze.Hide(Front, word, number) is { } hidden)
        {
            FrontBox.Text = hidden;
            HideWordBox.Text = "";
        }
        else
        {
            clozeMessage = $"“{word}” isn't in the text (or is already hidden).";
            ClozeCountText.Text = clozeMessage;
            ClozeCountText.Colour(TextBlock.ForegroundProperty, "CoralBrush");
        }
        HideWordBox.Focus();
    }

    // ---- Saving and closing ----

    private void Primary_Click(object? sender, RoutedEventArgs e)
    {
        if (IsAdding) SaveAndNext();
        else SaveEdit();
    }

    private void Secondary_Click(object? sender, RoutedEventArgs e)
    {
        // "Done" also keeps a card you typed but didn't save yet (like Rev on iPhone).
        if (IsAdding && CanSave) SaveAndNext();
        Close();
    }

    private void SaveAndNext()
    {
        if (!CanSave) return;
        addedCount += App.Database.AddNote(deck.Id, kind, Front, Back, Tags.Parse(TagsText));
        App.Database.SetPreference("lastCardKind", kind.ToString());
        allCards = App.Database.GetCards();
        // Ready for the next card: the text is cleared, the kind and tags stay.
        FrontBox.Text = "";
        BackBox.Text = "";
        HideWordBox.Text = "";
        UpdateState();
        UpdateTagChips();
        FrontBox.Focus();
    }

    private void SaveEdit()
    {
        if (!CanSave || editing is null) return;
        App.Database.SaveEdit(editing, Front, Back, Tags.Parse(TagsText));
        closingChecked = true;
        Close(true);
    }

    private bool HasUnsavedText => IsAdding
        ? Front.Trim().Length > 0 || Back.Trim().Length > 0
        : (Front, Back, TagsText) != original;

    /// <summary>Closing with text you haven't saved asks first, so nothing is lost by accident.</summary>
    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (closingChecked || !HasUnsavedText) return;
        e.Cancel = true;
        bool discard = await RevDialog.Confirm(this,
            IsAdding ? "Discard this card?" : "Discard your changes?",
            IsAdding ? "You've typed a card that hasn't been saved." : "Your changes to this card haven't been saved.",
            "Discard", destructive: true);
        if (!discard) return;
        closingChecked = true;
        Close();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DialogLayer.For(this) is { IsShowing: true }) return;   // the question on top has the keys
        bool ctrl = e.KeyModifiers == KeyModifiers.Control;
        bool inCardText = FrontBox.IsTyping || BackBox.IsTyping;
        if (ctrl && e.Key == Key.Enter) Primary_Click(this, e);
        else if (ctrl && e.Key == Key.B && inCardText) ApplyFormat("**");
        else if (ctrl && e.Key == Key.I && inCardText) ApplyFormat("*");
        else if (e.Key == Key.Escape) Close();
        else return;
        e.Handled = true;
    }

    private void FitOnScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
        Width = Math.Min(Width, area.Width * 0.92);
        Height = Math.Min(Height, area.Height * 0.92);
    }
}
