using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Rev.Core.Models;
using Rev.Core.Review;
using Rev.Core.Scheduling;
using Rev.Core.Text;
using Rev.Linux.Controls;

namespace Rev.Linux.Views;

/// <summary>
/// Studies one deck (or all decks). The rules (which card, what an answer does, undo) live in
/// ReviewSession in Rev.Core; this screen draws them and turns keys and clicks into actions.
/// </summary>
public partial class ReviewView : UserControl, IScreen
{
    private readonly ReviewSession session;
    private bool answerShown;
    private bool busy;   // a dialog or the editor is open: keys wait

    /// <summary>The colours of Again, Hard, Good and Easy (background, text).</summary>
    private static readonly (Rating Rating, string Title, string Background, string Foreground)[] Ratings =
    [
        (Rating.Again, "Again", "CoralBrush", "OnCoralBrush"),
        (Rating.Hard, "Hard", "MustardBrush", "OnMustardBrush"),
        (Rating.Good, "Good", "TealBrush", "OnTealBrush"),
        (Rating.Easy, "Easy", "SageBrush", "OnSageBrush"),
    ];

    public ReviewView() : this(null) { }

    /// <param name="deck">The deck to study, or null for "Review all decks".</param>
    public ReviewView(Deck? deck)
    {
        InitializeComponent();
        TitleText.Text = deck?.Name ?? "Review";
        session = new ReviewSession(App.Database, deck?.Id);
        AddHandler(KeyDownEvent, Screen_KeyDown, RoutingStrategies.Tunnel);
        Loaded += (_, _) => ShowCurrent();
    }

    /// <summary>Coming back to the screen (e.g. the next morning): pick up any newly due cards.</summary>
    public void Refresh()
    {
        if (session.Current is null)
        {
            session.LoadNext();
            ShowCurrent();
        }
    }

    // ---- Drawing ----

    private void ShowCurrent()
    {
        answerShown = false;
        UpdateCounts();
        UndoButton.IsEnabled = session.CanUndo;

        var card = session.Current;
        bool studying = card is not null;
        CardFace.IsVisible = studying;
        FinishedPanel.IsVisible = !studying;
        ShowAnswerButton.IsVisible = studying;
        RatingGrid.IsVisible = false;
        EditButton.IsEnabled = studying;
        KeysHint.Opacity = studying ? 1 : 0;

        if (card is null)
        {
            ShowFinished();
            Focus();
            return;
        }

        FrontView.Text = card.Front;
        BackView.Text = card.Back;
        AnswerPanel.IsVisible = false;
        TypedResult.IsVisible = false;
        bool typeIn = card.Kind == CardKind.TypeIn;
        TypeBox.IsVisible = typeIn;
        TypeBox.Text = "";
        if (typeIn) TypeBox.Focus(); else Focus();
    }

    private void UpdateCounts()
    {
        CountsPanel.Children.Clear();
        CountsPanel.Children.Add(CountPill("new", session.Queue.NewCards.Count, "CoralBrush"));
        CountsPanel.Children.Add(CountPill("learning", session.Queue.LearningCount, "MustardBrush"));
        CountsPanel.Children.Add(CountPill("review", session.Queue.ReviewDue.Count, "TealBrush"));
    }

    /// <summary>A coloured dot with a count, e.g. "● 3 new".</summary>
    private static Border CountPill(string label, int count, string colour)
    {
        var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }
            .Colour(Shape.FillProperty, colour);
        var text = new TextBlock { Text = $"{count} {label}", FontSize = 14, FontWeight = FontWeight.SemiBold };
        return new Border
        {
            Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(17),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text } },
        }.Colour(Border.BackgroundProperty, "SoftFillBrush").Named(text.Text);
    }

    private void ShowFinished()
    {
        if (!session.HasAnyCards)
        {
            FinishedIcon.Kind = MaterialIconKind.PackageVariant;   // an empty box
            FinishedTitle.Text = "No cards yet";
            FinishedMessage.Text = "Add some cards to a deck, then come back here.";
            CheckAgainButton.IsVisible = false;
            return;
        }
        int later = session.Queue.LaterToday;
        FinishedIcon.Kind = MaterialIconKind.CheckCircleOutline;
        FinishedTitle.Text = "All caught up!";
        var lines = new List<string>();
        if (session.ReviewedThisSession > 0) lines.Add($"You reviewed {session.ReviewedThisSession.Counted("card")} this session.");
        lines.Add(later > 0
            ? $"{later.Counted("learning card")} {(later == 1 ? "comes" : "come")} back later today."
            : "Come back tomorrow for more.");
        FinishedMessage.Text = string.Join("\n", lines);
        CheckAgainButton.IsVisible = later > 0;
    }

    // ---- Revealing and answering ----

    private void Reveal()
    {
        if (answerShown || session.Current is not { } card) return;
        answerShown = true;

        if (card.Kind == CardKind.TypeIn)
        {
            // What you typed, teal with a tick if it matched, coral with a cross if not.
            string typed = (TypeBox.Text ?? "").Trim();
            bool correct = TextTools.AnswerMatches(typed, card.Back);
            TypeBox.IsVisible = false;
            TypedResult.IsVisible = true;
            TypedIcon.Kind = correct ? MaterialIconKind.Check : MaterialIconKind.Close;
            TypedText.Text = typed.Length == 0 ? "(nothing typed)" : typed;
            string colour = correct ? "TealBrush" : "CoralBrush";
            TypedIcon.Colour(ForegroundProperty, colour);
            TypedText.Colour(TextBlock.ForegroundProperty, colour);
        }

        AnswerPanel.IsVisible = true;
        ShowAnswerButton.IsVisible = false;
        BuildRatingButtons();
        RatingGrid.IsVisible = true;
        Focus();
    }

    /// <summary>Again / Hard / Good / Easy, each with when you'd see the card next ("10 min", "4 d").</summary>
    private void BuildRatingButtons()
    {
        var previews = session.Previews();
        RatingGrid.Children.Clear();
        foreach (var (rating, title, background, foreground) in Ratings)
        {
            var button = Ui.Button("rev rating", new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = previews.GetValueOrDefault(rating, " "), FontSize = 14, FontWeight = FontWeight.SemiBold,
                                    Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) },
                },
            }).Named(title);
            button.Margin = new Thickness(5, 0, 5, 0);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Focusable = false;
            button.Colour(BackgroundProperty, background).Colour(ForegroundProperty, foreground);
            ToolTip.SetTip(button, $"Key {(int)rating}");
            button.Click += (_, _) => _ = Rate(rating);
            RatingGrid.Children.Add(button);
        }
    }

    private async Task Rate(Rating rating)
    {
        if (!answerShown || session.Current is not { } card) return;
        bool becameLeech = session.Rate(rating);
        ShowCurrent();
        if (becameLeech) await AskAboutLeech(card.Id);
    }

    /// <summary>A card you keep forgetting: offer to rewrite or suspend it.</summary>
    private async Task AskAboutLeech(Guid cardId)
    {
        if (App.Database.GetCard(cardId) is not { } card) return;
        busy = true;
        int choice = await RevDialog.Choose(this, "This card is a leech",
            $"You've forgotten it {card.Lapses} times. Cards like this usually need rewriting: make them shorter, " +
            "split them up, or add a memory hook. Suspending hides it from reviews until you unsuspend it in its deck.",
            "Keep studying", "Suspend it", "Rewrite it");
        busy = false;
        if (choice == 1)
        {
            session.Suspend(card.Id);
            ShowCurrent();
        }
        else if (choice == 2) await OpenEditor(card);
        Focus();
    }

    // ---- Undo and edit ----

    private void Undo()
    {
        if (!session.CanUndo) return;
        session.Undo();
        ShowCurrent();
    }

    /// <summary>Fix a typo or improve the card you're looking at, without leaving the review.</summary>
    private async Task EditCurrent()
    {
        if (session.Current is { } card) await OpenEditor(card);
    }

    private async Task OpenEditor(Card card)
    {
        var deck = App.Database.GetDecks().FirstOrDefault(d => d.Id == card.DeckId);
        if (deck is null) return;
        bool wasShown = answerShown;
        busy = true;
        await new CardEditorWindow(deck, card).ShowDialog(MainWindow.Current);
        busy = false;
        session.ReloadCurrent();
        ShowCurrent();
        if (wasShown && session.Current?.Id == card.Id) Reveal();   // stay where you were
    }

    // ---- Keys and clicks ----

    private void Screen_KeyDown(object? sender, KeyEventArgs e)
    {
        if (busy || DialogLayer.For(this) is { IsShowing: true }) return;
        // While typing an answer, keys are letters; only Enter (reveal) and Esc (back) are special.
        bool typing = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() == TypeBox;
        if (typing && e.Key is not (Key.Enter or Key.Escape)) return;

        bool ctrl = e.KeyModifiers == KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.Space or Key.Enter when !ctrl:
                Reveal();
                break;
            case Key.D1 or Key.NumPad1: _ = Rate(Rating.Again); break;
            case Key.D2 or Key.NumPad2: _ = Rate(Rating.Hard); break;
            case Key.D3 or Key.NumPad3: _ = Rate(Rating.Good); break;
            case Key.D4 or Key.NumPad4: _ = Rate(Rating.Easy); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.E when e.KeyModifiers == KeyModifiers.None: _ = EditCurrent(); break;
            case Key.Escape: MainWindow.Current.GoBack(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void CardFace_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source is TextBox) return;   // clicking into the answer box isn't "show the answer"
        Reveal();
    }

    private void ShowAnswer_Click(object? sender, RoutedEventArgs e) => Reveal();

    private void Undo_Click(object? sender, RoutedEventArgs e) => Undo();

    private void Edit_Click(object? sender, RoutedEventArgs e) => _ = EditCurrent();

    private void Back_Click(object? sender, RoutedEventArgs e) => MainWindow.Current.GoBack();

    private void CheckAgain_Click(object? sender, RoutedEventArgs e)
    {
        session.LoadNext();
        ShowCurrent();
    }
}
