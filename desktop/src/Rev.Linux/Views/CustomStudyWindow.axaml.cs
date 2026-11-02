using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rev.Core.Models;
using Rev.Core.Review;
using Rev.Core.Text;

namespace Rev.Linux.Views;

/// <summary>
/// Choose a custom study session. The Study button shows how many cards your choices pick.
/// Closes with the chosen <see cref="CustomStudy"/>, or null if you cancelled.
/// </summary>
public partial class CustomStudyWindow : Window
{
    private readonly Guid? deckId;
    private readonly string place;
    private readonly List<Card> cards;
    private readonly HashSet<string> pickedTags = new(StringComparer.OrdinalIgnoreCase);
    private CustomStudy study = new();
    private int limit = 20;
    private int days = 1;

    public CustomStudyWindow() : this(null) { }

    /// <param name="deck">The deck to study, or null for every deck.</param>
    public CustomStudyWindow(Deck? deck)
    {
        InitializeComponent();
        deckId = deck?.Id;
        DeckText.Text = deck?.Name ?? "All decks";
        place = deck is null ? "your decks" : "this deck";
        cards = App.Database.GetCards(deckId);
        foreach (var tag in CustomStudy.AllTags(cards))
        {
            var chip = new ToggleButton { Content = tag, Tag = tag };
            chip.Classes.Add("choice");
            chip.IsCheckedChanged += (_, _) =>
            {
                if (chip.IsChecked == true) pickedTags.Add(tag); else pickedTags.Remove(tag);
                Update();
            };
            TagChoices.Children.Add(chip);
        }
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && StartButton.IsEnabled) Start_Click(this, e);
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Update();
        // Start on the first choice that has cards, e.g. "Study by tag" in a deck with no new cards.
        var first = Enum.GetValues<CustomStudyKind>().FirstOrDefault(k => Count(k) > 0, CustomStudyKind.MoreNew);
        KindChoices.Children.OfType<RadioButton>().First(rb => (string?)rb.Tag == first.ToString()).IsChecked = true;
    }

    /// <summary>How many cards a choice would pick with the current numbers and tags.</summary>
    private int Count(CustomStudyKind kind) => (study with { Kind = kind }).PickCards(App.Database, cards, DateTime.Now).Count;

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
        TagPanel.IsVisible = study.Kind == CustomStudyKind.ByTag;
        TagsHint.Text = TagChoices.Children.Count == 0
            ? "No tags yet. Add tags to cards in the card editor."
            : pickedTags.Count == 0 ? "No tags picked: any card can be chosen." : "Cards with any of the picked tags.";

        // Each choice shows how many cards it would pick, e.g. "More new cards  0".
        foreach (var choice in KindChoices.Children.OfType<RadioButton>())
        {
            var kind = Enum.Parse<CustomStudyKind>((string)choice.Tag!);
            choice.Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = CustomStudy.Title(kind) },
                    new TextBlock { Text = Count(kind).ToString(), FontWeight = Avalonia.Media.FontWeight.Bold,
                                    Opacity = 0.65, Margin = new Avalonia.Thickness(10, 0, 0, 0) },
                },
            };
            ToolTip.SetTip(choice, CustomStudy.Subtitle(kind));
        }

        int count = study.PickCards(App.Database, cards, DateTime.Now).Count;
        StartButton.Content = $"Study {count.Counted("card")}";
        StartButton.IsEnabled = count > 0;
        ReasonBox.IsVisible = count == 0;
        if (count == 0) ReasonText.Text = study.EmptyReason(cards, place, DateTime.Now);
    }

    private void Change(int step)
    {
        if (UsesDays) days = CustomStudy.StepDays(days, up: step > 0);
        else limit = Math.Clamp(limit + step * 5, 5, 1000);
        Update();
    }

    private void Fewer_Click(object? sender, RoutedEventArgs e) => Change(-1);

    private void More_Click(object? sender, RoutedEventArgs e) => Change(1);

    private void Kind_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string kind })
        {
            study = study with { Kind = Enum.Parse<CustomStudyKind>(kind) };
            Update();
        }
    }

    private void TagCards_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string which })
        {
            study = study with { TagCards = Enum.Parse<TagCards>(which) };
            Update();
        }
    }

    private void Order_Changed(object? sender, RoutedEventArgs e)
    {
        study = study with { Shuffled = OldestFirstChoice?.IsChecked != true };
        Update();
    }

    private void Start_Click(object? sender, RoutedEventArgs e) => Close(study);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
