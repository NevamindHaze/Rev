using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rev.App.Controls;
using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.App.Views;

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
    private TextBox lastTextBox;          // where Bold / Italic apply
    private int addedCount;
    private bool closingChecked;          // so the "discard?" question is asked only once
    private (string Front, string Back, string Tags) original = ("", "", "");

    public CardEditorWindow(Deck deck, Card? cardToEdit)
    {
        InitializeComponent();
        this.deck = deck;
        editing = cardToEdit;
        allCards = App.Database.GetCards();
        lastTextBox = FrontBox;
        FitOnScreen();

        DeckText.Text = deck.Name;
        if (editing is { } card)
        {
            Title = HeaderText.Text = "Edit card";
            kind = card.Kind;
            KindChoices.Visibility = Visibility.Collapsed;
            KindLabel.Visibility = Visibility.Visible;
            KindLabel.Text = KindTitle(kind);
            bool cloze = card.Kind == CardKind.Cloze && card.ClozeSource != null;
            FrontBox.Text = cloze ? card.ClozeSource! : card.Front;
            BackBox.Text = cloze ? card.ClozeExtra ?? "" : card.Back;
            TagsBox.Text = card.TagsText;
            original = (FrontBox.Text, BackBox.Text, TagsBox.Text);
            SecondaryButton.Content = "Cancel";
            PrimaryButton.Content = "Save";
            PrimaryButton.Style = (Style)FindResource("PillButton");
        }
        else
        {
            // Start with the kind you used last time.
            kind = Enum.TryParse(App.Database.GetPreference("lastCardKind"), out CardKind last) ? last : CardKind.Basic;
            KindChoices.Children.OfType<RadioButton>().First(r => (string)r.Tag == kind.ToString()).IsChecked = true;
        }

        UpdateForKind();
        UpdateTagChips();
        PreviewKeyDown += Window_PreviewKeyDown;
        Closing += Window_Closing;
        Loaded += (_, _) => FrontBox.Focus();
    }

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

    private int ClozeCount => Cloze.Numbers(FrontBox.Text).Count;

    private bool CanSave => kind == CardKind.Cloze ? ClozeCount > 0 : FrontBox.Text.Trim().Length > 0;

    private void UpdateForKind()
    {
        bool cloze = kind == CardKind.Cloze;
        KindExplanation.Text = Explanation(kind);
        FrontLabel.Text = cloze ? "Text" : "Front";
        BackLabel.Text = cloze ? "Extra (optional, shown on the back)" : "Back";
        ClozeTools.Visibility = cloze ? Visibility.Visible : Visibility.Collapsed;
        UpdateState();
    }

    /// <summary>Button titles, the cloze count and whether saving is possible, after any change.</summary>
    private void UpdateState()
    {
        int count = ClozeCount;
        int next = Cloze.NextNumber(FrontBox.Text);
        bool hasWord = HideWordBox.Text.Trim().Length > 0;
        HideButton.Content = $"Hide c{next}";
        HideButton.IsEnabled = hasWord;
        AddToButton.Content = $"Add to c{next - 1}";
        AddToButton.Visibility = next > 1 ? Visibility.Visible : Visibility.Collapsed;
        AddToButton.IsEnabled = hasWord;
        if (ClozeCountText.Tag is not "message")
        {
            ClozeCountText.Text = count == 0 ? "Nothing hidden yet." : $"This makes {count.Counted("card")}.";
            ClozeCountText.SetResourceReference(TextBlock.ForegroundProperty, count == 0 ? "CoralBrush" : "TealBrush");
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
        foreach (var tag in Tags.Suggestions(allCards, TagsBox.Text))
        {
            var chip = new Button
            {
                Style = (Style)FindResource("SecondaryPillButton"),
                MinHeight = 36, FontSize = 14, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 8),
                Content = "+ " + tag, ToolTip = $"Add the tag “{tag}”",
            };
            chip.Click += (_, _) =>
            {
                TagsBox.Text = Tags.Format(Tags.Parse(TagsBox.Text).Append(tag));
                TagsBox.CaretIndex = TagsBox.Text.Length;
            };
            TagChips.Children.Add(chip);
        }
    }

    // ---- Typing ----

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        kind = Enum.Parse<CardKind>((string)((RadioButton)sender).Tag);
        if (IsLoaded) UpdateForKind();
    }

    private void Text_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ClozeCountText.Tag = null;
        UpdateState();
    }

    private void TagsBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateTagChips();

    private void TextBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => lastTextBox = (TextBox)sender;

    // ---- Bold and italic ----

    private void Bold_Click(object sender, RoutedEventArgs e) => ApplyFormat("**");

    private void Italic_Click(object sender, RoutedEventArgs e) => ApplyFormat("*");

    /// <summary>Wraps the selected words in ** (bold) or * (italic), or removes them if already there.</summary>
    private void ApplyFormat(string marker)
    {
        var box = lastTextBox;
        var (text, start, length) = TextTools.ToggleFormat(box.Text, marker, box.SelectionStart, box.SelectionLength);
        box.Text = text;
        box.Focus();
        box.Select(start, length);
    }

    // ---- Cloze helper ----

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide(Cloze.NextNumber(FrontBox.Text));

    private void AddTo_Click(object sender, RoutedEventArgs e) => Hide(Cloze.NextNumber(FrontBox.Text) - 1);

    private void HideWordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Hide(Cloze.NextNumber(FrontBox.Text));
        e.Handled = true;
    }

    private void HideWordBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) UpdateState();
    }

    /// <summary>Wraps the first not-yet-hidden appearance of the word in {{cN::…}}.</summary>
    private void Hide(int number)
    {
        string word = HideWordBox.Text.Trim();
        if (word.Length == 0) return;
        if (Cloze.Hide(FrontBox.Text, word, number) is { } hidden)
        {
            FrontBox.Text = hidden;
            HideWordBox.Text = "";
        }
        else
        {
            ClozeCountText.Tag = "message";
            ClozeCountText.Text = $"“{word}” isn't in the text (or is already hidden).";
            ClozeCountText.SetResourceReference(TextBlock.ForegroundProperty, "CoralBrush");
        }
        HideWordBox.Focus();
    }

    // ---- Saving and closing ----

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (IsAdding) SaveAndNext();
        else SaveEdit();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        // "Done" also keeps a card you typed but didn't save yet (like Rev on iPhone).
        if (IsAdding && CanSave) SaveAndNext();
        Close();
    }

    private void SaveAndNext()
    {
        if (!CanSave) return;
        addedCount += App.Database.AddNote(deck.Id, kind, FrontBox.Text, BackBox.Text, Tags.Parse(TagsBox.Text));
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
        App.Database.SaveEdit(editing, FrontBox.Text, BackBox.Text, Tags.Parse(TagsBox.Text));
        closingChecked = true;
        DialogResult = true;
    }

    private bool HasUnsavedText => IsAdding
        ? FrontBox.Text.Trim().Length > 0 || BackBox.Text.Trim().Length > 0
        : (FrontBox.Text, BackBox.Text, TagsBox.Text) != original;

    /// <summary>Closing with text you haven't saved asks first, so nothing is lost by accident.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closingChecked || !HasUnsavedText) return;
        bool discard = RevDialog.Confirm(this,
            IsAdding ? "Discard this card?" : "Discard your changes?",
            IsAdding ? "You've typed a card that hasn't been saved." : "Your changes to this card haven't been saved.",
            "Discard", destructive: true);
        if (discard) closingChecked = true;
        else e.Cancel = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        bool inCardText = Keyboard.FocusedElement == FrontBox || Keyboard.FocusedElement == BackBox;
        if (ctrl && e.Key == Key.Enter) Primary_Click(this, e);
        else if (ctrl && e.Key == Key.B && inCardText) ApplyFormat("**");
        else if (ctrl && e.Key == Key.I && inCardText) ApplyFormat("*");
        else if (e.Key == Key.Escape) Close();
        else return;
        e.Handled = true;
    }

    private void FitOnScreen()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width * 0.92);
        Height = Math.Min(Height, area.Height * 0.92);
    }
}
