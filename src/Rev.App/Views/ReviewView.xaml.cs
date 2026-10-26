using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Rev.App.Controls;
using Rev.Core.Models;
using Rev.Core.Review;
using Rev.Core.Scheduling;
using Rev.Core.Text;

namespace Rev.App.Views;

/// <summary>
/// Studies one deck (or all decks). The rules (which card, what an answer does, undo) live in
/// ReviewSession in Rev.Core; this screen draws them and turns keys and clicks into actions.
/// </summary>
public partial class ReviewView : UserControl, IScreen
{
    private readonly ReviewSession session;
    private bool answerShown;

    /// <summary>The colours of Again, Hard, Good and Easy (background, text).</summary>
    private static readonly (Rating Rating, string Title, string Background, string Foreground)[] Ratings =
    [
        (Rating.Again, "Again", "CoralBrush", "OnCoralBrush"),
        (Rating.Hard, "Hard", "MustardBrush", "OnMustardBrush"),
        (Rating.Good, "Good", "TealBrush", "OnTealBrush"),
        (Rating.Easy, "Easy", "SageBrush", "OnSageBrush"),
    ];

    /// <param name="deck">The deck to study, or null for "Review all decks".</param>
    public ReviewView(Deck? deck)
    {
        InitializeComponent();
        TitleText.Text = deck?.Name ?? "Review";
        session = new ReviewSession(App.Database, deck?.Id);
        PreviewKeyDown += Screen_PreviewKeyDown;
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
        CardFace.Visibility = studying ? Visibility.Visible : Visibility.Collapsed;
        FinishedPanel.Visibility = studying ? Visibility.Collapsed : Visibility.Visible;
        ShowAnswerButton.Visibility = studying ? Visibility.Visible : Visibility.Collapsed;
        RatingGrid.Visibility = Visibility.Collapsed;
        EditButton.IsEnabled = studying;
        KeysHint.Visibility = studying ? Visibility.Visible : Visibility.Hidden;

        if (card is null)
        {
            ShowFinished();
            Focus();
            return;
        }

        FrontView.Text = card.Front;
        BackView.Text = card.Back;
        AnswerPanel.Visibility = Visibility.Collapsed;
        TypedResult.Visibility = Visibility.Collapsed;
        bool typeIn = card.Kind == CardKind.TypeIn;
        TypeBox.Visibility = typeIn ? Visibility.Visible : Visibility.Collapsed;
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
        var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, colour);
        var text = new TextBlock { Text = $"{count} {label}", FontSize = 14, FontWeight = FontWeights.SemiBold };
        var pill = new Border
        {
            Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(17),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text } },
        };
        pill.SetResourceReference(Border.BackgroundProperty, "SoftFillBrush");
        AutomationProperties.SetName(pill, text.Text);
        return pill;
    }

    private void ShowFinished()
    {
        if (!session.HasAnyCards)
        {
            FinishedIcon.Text = "";   // an empty box
            FinishedTitle.Text = "No cards yet";
            FinishedMessage.Text = "Add some cards to a deck, then come back here.";
            CheckAgainButton.Visibility = Visibility.Collapsed;
            return;
        }
        int later = session.Queue.LaterToday;
        FinishedIcon.Text = "";       // a tick in a circle
        FinishedTitle.Text = "All caught up!";
        var lines = new List<string>();
        if (session.ReviewedThisSession > 0) lines.Add($"You reviewed {session.ReviewedThisSession.Counted("card")} this session.");
        lines.Add(later > 0
            ? $"{later.Counted("learning card")} {(later == 1 ? "comes" : "come")} back later today."
            : "Come back tomorrow for more.");
        FinishedMessage.Text = string.Join("\n", lines);
        CheckAgainButton.Visibility = later > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Revealing and answering ----

    private void Reveal()
    {
        if (answerShown || session.Current is not { } card) return;
        answerShown = true;

        if (card.Kind == CardKind.TypeIn)
        {
            // What you typed, teal with a tick if it matched, coral with a cross if not.
            bool correct = TextTools.AnswerMatches(TypeBox.Text, card.Back);
            TypeBox.Visibility = Visibility.Collapsed;
            TypedResult.Visibility = Visibility.Visible;
            TypedIcon.Text = correct ? "" : "";
            TypedText.Text = TypeBox.Text.Trim().Length == 0 ? "(nothing typed)" : TypeBox.Text.Trim();
            string colour = correct ? "TealBrush" : "CoralBrush";
            TypedIcon.SetResourceReference(TextBlock.ForegroundProperty, colour);
            TypedText.SetResourceReference(TextBlock.ForegroundProperty, colour);
        }

        AnswerPanel.Visibility = Visibility.Visible;
        ShowAnswerButton.Visibility = Visibility.Collapsed;
        BuildRatingButtons();
        RatingGrid.Visibility = Visibility.Visible;
        Focus();
    }

    /// <summary>Again / Hard / Good / Easy, each with when you'd see the card next ("10 min", "4 d").</summary>
    private void BuildRatingButtons()
    {
        var previews = session.Previews();
        RatingGrid.Children.Clear();
        foreach (var (rating, title, background, foreground) in Ratings)
        {
            var button = new Button
            {
                Style = (Style)FindResource("RatingButton"), Margin = new Thickness(5, 0, 5, 0), Focusable = false,
                ToolTip = $"Key {(int)rating}",
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = previews.GetValueOrDefault(rating, " "), FontSize = 14, FontWeight = FontWeights.SemiBold,
                                        Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) },
                    },
                },
            };
            button.SetResourceReference(BackgroundProperty, background);
            button.SetResourceReference(ForegroundProperty, foreground);
            AutomationProperties.SetName(button, title);
            button.Click += (_, _) => Rate(rating);
            RatingGrid.Children.Add(button);
        }
    }

    private void Rate(Rating rating)
    {
        if (!answerShown || session.Current is not { } card) return;
        bool becameLeech = session.Rate(rating);
        ShowCurrent();
        if (becameLeech) AskAboutLeech(card.Id);
    }

    /// <summary>A card you keep forgetting: offer to rewrite or suspend it.</summary>
    private void AskAboutLeech(Guid cardId)
    {
        if (App.Database.GetCard(cardId) is not { } card) return;
        int choice = RevDialog.Choose(Window.GetWindow(this), "This card is a leech",
            $"You've forgotten it {card.Lapses} times. Cards like this usually need rewriting: make them shorter, " +
            "split them up, or add a memory hook. Suspending hides it from reviews until you unsuspend it in its deck.",
            "Keep studying", "Suspend it", "Rewrite it");
        if (choice == 1)
        {
            session.Suspend(card.Id);
            ShowCurrent();
        }
        else if (choice == 2) OpenEditor(card);
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
    private void EditCurrent()
    {
        if (session.Current is { } card) OpenEditor(card);
    }

    private void OpenEditor(Card card)
    {
        var deck = App.Database.GetDecks().FirstOrDefault(d => d.Id == card.DeckId);
        if (deck is null) return;
        bool wasShown = answerShown;
        new CardEditorWindow(deck, card) { Owner = Window.GetWindow(this) }.ShowDialog();
        session.ReloadCurrent();
        ShowCurrent();
        if (wasShown && session.Current?.Id == card.Id) Reveal();   // stay where you were
    }

    // ---- Keys and clicks ----

    private void Screen_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // While typing an answer, keys are letters; only Enter (reveal) and Esc (back) are special.
        if (Keyboard.FocusedElement == TypeBox && e.Key is not (Key.Enter or Key.Escape)) return;

        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Space or Key.Enter when !ctrl:
                Reveal();
                break;
            case Key.D1 or Key.NumPad1: Rate(Rating.Again); break;
            case Key.D2 or Key.NumPad2: Rate(Rating.Hard); break;
            case Key.D3 or Key.NumPad3: Rate(Rating.Good); break;
            case Key.D4 or Key.NumPad4: Rate(Rating.Easy); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.E when Keyboard.Modifiers == ModifierKeys.None: EditCurrent(); break;
            case Key.Escape: MainWindow.Current.GoBack(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void CardFace_Click(object sender, MouseButtonEventArgs e) => Reveal();

    private void ShowAnswer_Click(object sender, RoutedEventArgs e) => Reveal();

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Edit_Click(object sender, RoutedEventArgs e) => EditCurrent();

    private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.GoBack();

    private void CheckAgain_Click(object sender, RoutedEventArgs e)
    {
        session.LoadNext();
        ShowCurrent();
    }
}
