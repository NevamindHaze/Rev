using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Rev.Core.Models;
using Rev.Core.Review;
using Rev.Core.Text;

namespace Rev.App.Views;

/// <summary>
/// Choose a custom study session. The Study button shows how many cards your choices pick.
/// After ShowDialog() returns true, <see cref="Study"/> is the chosen session.
/// </summary>
public partial class CustomStudyWindow : Window
{
    private readonly List<Card> cards;
    private readonly HashSet<string> pickedTags = new(StringComparer.OrdinalIgnoreCase);
    private CustomStudy study = new();
    private int limit = 20;
    private int days = 1;

    /// <param name="deck">The deck to study, or null for every deck.</param>
    public CustomStudyWindow(Deck? deck)
    {
        InitializeComponent();
        DeckText.Text = deck?.Name ?? "All decks";
        cards = App.Database.GetCards(deck?.Id);
        foreach (var tag in CustomStudy.AllTags(cards))
        {
            var chip = new ToggleButton { Content = tag, Style = (Style)FindResource("ChoicePill") };
            chip.Checked += (_, _) => { pickedTags.Add(tag); Update(); };
            chip.Unchecked += (_, _) => { pickedTags.Remove(tag); Update(); };
            TagChoices.Children.Add(chip);
        }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && StartButton.IsEnabled) Start_Click(this, e);
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        };
        Update();
    }

    /// <summary>The chosen session.</summary>
    public CustomStudy Study => study;

    private bool UsesDays => study.Kind is CustomStudyKind.Forgotten or CustomStudyKind.ReviewAhead;

    /// <summary>Reads the choices into <see cref="study"/> and updates the labels and the Study button.</summary>
    private void Update()
    {
        if (StartButton is null) return;   // still loading
        study = study with { Limit = limit, Days = days, Tags = pickedTags.ToList() };

        ExplainText.Text = study.Kind switch
        {
            CustomStudyKind.MoreNew => "Learn new cards beyond today's limit, oldest first.",
            CustomStudyKind.Forgotten => "Go over every card you pressed Again on in this time.",
            CustomStudyKind.ReviewAhead => "Review cards that aren't due yet, soonest first. Handy before an exam or a trip.",
            _ => "Study the cards with any of the tags you pick.",
        };
        NumberLabel.Text = study.Kind switch
        {
            CustomStudyKind.MoreNew => "New cards",
            CustomStudyKind.Forgotten => "Days to look back",
            CustomStudyKind.ReviewAhead => "Days ahead",
            _ => "At most",
        };
        NumberText.Text = (UsesDays ? days : limit).ToString();
        TagPanel.Visibility = study.Kind == CustomStudyKind.ByTag ? Visibility.Visible : Visibility.Collapsed;
        TagsHint.Text = TagChoices.Children.Count == 0
            ? "No tags yet. Add tags to cards in the card editor."
            : pickedTags.Count == 0 ? "No tags picked: any card can be chosen." : "Cards with any of the picked tags.";

        int count = study.PickCards(App.Database, cards, DateTime.Now).Count;
        StartButton.Content = $"Study {count.Counted("card")}";
        StartButton.IsEnabled = count > 0;
    }

    private void Change(int step)
    {
        if (UsesDays) days = Math.Clamp(days + step, 1, 30);
        else limit = Math.Clamp(limit + step * 5, 5, 1000);
        Update();
    }

    private void Fewer_Click(object sender, RoutedEventArgs e) => Change(-1);

    private void More_Click(object sender, RoutedEventArgs e) => Change(1);

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string kind })
        {
            study = study with { Kind = Enum.Parse<CustomStudyKind>(kind) };
            Update();
        }
    }

    private void TagCards_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string which })
        {
            study = study with { TagCards = Enum.Parse<TagCards>(which) };
            Update();
        }
    }

    private void Order_Checked(object sender, RoutedEventArgs e)
    {
        study = study with { Shuffled = OldestFirstChoice?.IsChecked != true };
        Update();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
