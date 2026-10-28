using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Linux.Controls;
using Rev.Linux.Views;

namespace Rev.Linux.Tests;

/// <summary>Every screen of Rev for Linux, used with the keyboard and mouse like you would.</summary>
public sealed class ScreenTests : ScreenTest
{
    [AvaloniaFact]
    public void HomeShowsTheGreetingNumbersAndDecks()
    {
        AddSampleDecks();
        Db.SaveSettings(Db.GetSettings() with { UserName = "Marshal" });
        var home = Assert.IsType<HomeView>(CurrentScreen);
        string text = Texts(home);
        Assert.Contains("Hi, Marshal 👋", text);
        Assert.Contains("0 due · 7 new · 0 done today", text);
        var rows = All<DeckListRow>(home).ToList();
        Assert.Equal(["Pharmacology", "Parasitology"], rows.Select(r => r.Deck.Name));
        Assert.Contains("4", Texts(rows[0]).Split(" | "));   // Total: basic + reversed (2) + type-in
        Picture(Window, "01-home");
    }

    [AvaloniaFact]
    public void ControlsAreDesktopSized()
    {
        AddSampleDecks();
        var buttons = All<Button>(Window).Where(b => b.Classes.Contains("rev") && !b.Classes.Contains("tile") && b.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, b => Assert.InRange(b.Bounds.Height, 24, 38));
    }

    [AvaloniaFact]
    public void TheSidebarListsDecksWithWhatsDue()
    {
        AddSampleDecks();
        var rows = All<SidebarDeckRow>(Window).ToList();
        Assert.Equal(["Pharmacology", "Parasitology"], rows.Select(r => r.Deck.Name));
        Click(rows[1]);
        Assert.IsType<DeckView>(CurrentScreen);
        Assert.Contains("Parasitology", Texts(CurrentScreen!));
    }

    [AvaloniaFact]
    public void ShortcutsOpenEachPage()
    {
        AddSampleDecks();
        _ = Window;
        Press(Key.D2, KeyModifiers.Control); Assert.IsType<AllCardsView>(CurrentScreen);
        Press(Key.D3, KeyModifiers.Control); Assert.IsType<StatsView>(CurrentScreen);
        Press(Key.OemComma, KeyModifiers.Control); Assert.IsType<SettingsView>(CurrentScreen);
        Press(Key.D1, KeyModifiers.Control); Assert.IsType<HomeView>(CurrentScreen);
        Press(Key.B, KeyModifiers.Control); Assert.False(Window.IsSidebarOpen);
        Press(Key.B, KeyModifiers.Control); Assert.True(Window.IsSidebarOpen);
    }

    [AvaloniaFact]
    public void DeletingTheOpenDeckFromTheSidebarGoesHome()
    {
        AddSampleDecks();
        Window.ShowDeck(Pharmacology);
        Settle();
        var row = All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Pharmacology");
        row.Focus();
        Press(Key.Delete);
        Click(Find<Button>(OpenDialog(Window)!, "Delete"));
        Assert.IsType<HomeView>(CurrentScreen);
        Assert.DoesNotContain(All<SidebarDeckRow>(Window), r => r.Deck.Name == "Pharmacology");
    }

    [AvaloniaFact]
    public void TheSidebarsDueCountsFollowChanges()
    {
        AddSampleDecks();
        var before = All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Parasitology").ToStudyCount;
        Db.AddNote(Parasitology.Id, CardKind.Basic, "Host of *Dipylidium*?", "Flea", []);
        Window.RefreshSidebar();   // what SyncRunner.Synced and closing the editor call
        Settle();
        Assert.Equal(before + 1, All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Parasitology").ToStudyCount);
    }

    [AvaloniaFact]
    public void AnEmptyRevInvitesYouToMakeADeck()
    {
        Assert.Contains("No decks yet", Texts(Window));
        Assert.Empty(All<SidebarDeckRow>(Window));
        Assert.True(Find<Button>(Window, "New deck").IsEnabled);
        Picture(Window, "02-home-empty");
    }

    [AvaloniaFact]
    public void CtrlNMakesADeckThroughTheDialog()
    {
        AddSampleDecks();
        _ = Window;
        Press(Key.N, KeyModifiers.Control);
        var dialog = OpenDialog(Window);
        Assert.NotNull(dialog);
        Assert.Contains("New deck", Texts(dialog!));
        Type("Microbiology");
        Picture(Window, "03-new-deck-dialog");
        Press(Key.Enter);
        Assert.Null(OpenDialog(Window));
        Assert.Contains(Db.GetDecks(), d => d.Name == "Microbiology");
        Assert.Contains(All<SidebarDeckRow>(Window), r => r.Deck.Name == "Microbiology");
        Assert.IsType<DeckView>(CurrentScreen);   // the new deck opens, ready for cards
    }

    [AvaloniaFact]
    public void EscCancelsADialogAndAnEmptyNameCantBeSaved()
    {
        _ = Window;
        Press(Key.N, KeyModifiers.Control);
        var create = Find<Button>(OpenDialog(Window)!, "Create");
        Assert.False(create.IsEnabled);   // nothing typed yet
        Press(Key.Escape);
        Assert.Null(OpenDialog(Window));
        Assert.Empty(Db.GetDecks());
    }

    [AvaloniaFact]
    public void DeletingADeckAsksFirstAndEnterMeansCancel()
    {
        AddSampleDecks();
        var row = All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Pharmacology");
        row.Focus();
        Press(Key.Delete);
        Assert.Contains("Delete “Pharmacology”?", Texts(OpenDialog(Window)!));
        Picture(Window, "04-delete-deck-dialog");
        Press(Key.Enter);   // the keyboard starts on Cancel, so Enter can never delete by accident
        Assert.Equal(2, Db.GetDecks().Count);

        row = All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Pharmacology");
        row.Focus();
        Press(Key.Delete);
        Click(Find<Button>(OpenDialog(Window)!, "Delete"));
        Assert.Equal(["Parasitology"], Db.GetDecks().Select(d => d.Name));
    }

    [AvaloniaFact]
    public void ADeckShowsItsNumbersAndCards()
    {
        AddSampleDecks();
        Click(All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Parasitology"));
        var deck = Assert.IsType<DeckView>(CurrentScreen);
        string text = Texts(deck);
        Assert.Contains("Parasitology", text);
        Assert.Contains("3 cards · 0 due · 3 new", text);   // the cloze makes 2 cards, plus a basic one
        Assert.True(Find<Button>(deck, "Study").IsEnabled);
        Assert.True(Find<Button>(deck, "More").IsEnabled);
        Assert.Contains("Cloze", text);
        var cloze = All<Button>(deck).First(b => b.Tag is Guid && ToolTip.GetTip(b) is string tip && tip.Contains("#flukes"));
        Assert.NotNull(cloze);   // tags show in the row's tooltip
        Picture(Window, "05-deck");
        Press(Key.D1, KeyModifiers.Control);
        Assert.IsType<HomeView>(CurrentScreen);
    }

    [AvaloniaFact]
    public void TheCardBrowserSearchesAndFilters()
    {
        AddSampleDecks();
        Window.Open(new AllCardsView());
        Settle();
        var browser = All<CardBrowser>(Window).Single();
        Assert.Contains("7 cards", Texts(browser));
        var search = Find<TextBox>(browser, "Search cards");
        Assert.True(search.IsFocused, "the search box has the keyboard on this screen");
        Type("meloxicam");
        Assert.Equal("meloxicam", search.Text);
        Wait(400);   // the search waits a moment after typing
        Assert.Contains("1 card of 7", Texts(browser));
        Picture(Window, "06-all-cards-search");
    }

    [AvaloniaFact]
    public void TheCardTableSortsByAColumnAndEnterEdits()
    {
        AddSampleDecks();
        Window.Open(new AllCardsView());
        Settle();
        var browser = All<CardBrowser>(Window).Single();
        Click(Find<Button>(browser, "Front"));
        var rows = All<Button>(browser).Where(b => b.Tag is Guid).ToList();
        var fronts = rows.Select(r => AutomationProperties.GetName(r)!.Replace("*", "")).ToList();
        Assert.Equal(fronts.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase), fronts);
        Assert.Contains("A to Z", Texts(browser));   // the sort menu shows the sort the heading chose
        rows[0].Focus();
        Press(Key.Enter);
        Assert.NotNull(Opened<CardEditorWindow>());
    }

    [AvaloniaFact]
    public void ReviewingWithTheKeyboard()
    {
        AddSampleDecks();
        Window.Open(new ReviewView(Pharmacology));
        Settle();
        var review = Assert.IsType<ReviewView>(CurrentScreen);
        Assert.Contains("Studying Pharmacology", Texts(review));
        Assert.Contains("QUESTION", Texts(review));
        Assert.DoesNotContain("ANSWER", Texts(review));
        Picture(Window, "07-review-question");

        Press(Key.Space);
        Assert.Contains("ANSWER", Texts(review));
        Assert.Contains("1 · Again", Texts(review));
        Picture(Window, "08-review-answer");

        int before = Db.ReviewsToday(DateTime.Now);
        Press(Key.D3);   // Good
        Assert.Equal(before + 1, Db.ReviewsToday(DateTime.Now));
        Assert.DoesNotContain("ANSWER", Texts(review));

        Press(Key.Z, KeyModifiers.Control);   // undo
        Assert.Equal(before, Db.ReviewsToday(DateTime.Now));

        Press(Key.Escape);   // stops: back to the deck's page
        Assert.IsType<DeckView>(CurrentScreen);
    }

    [AvaloniaFact]
    public void EscWithAQuestionOpenOnlyClosesTheQuestion()
    {
        AddSampleDecks();
        Window.Open(new ReviewView(Pharmacology));
        Settle();
        _ = RevDialog.Confirm(Window, "Discard this card?", "You've typed a card that hasn't been saved.", "Discard", destructive: true);
        Settle();
        Press(Key.Escape);
        Assert.Null(OpenDialog(Window));
        Assert.IsType<ReviewView>(CurrentScreen);
    }

    [AvaloniaFact]
    public void TypeTheAnswerCardsCheckWhatYouTyped()
    {
        Pharmacology = Db.AddDeck("Pharmacology");
        Db.AddNote(Pharmacology.Id, Core.Models.CardKind.TypeIn, "Antidote for paracetamol toxicity in cats?", "Acetylcysteine", []);
        Window.Open(new ReviewView(Pharmacology));
        Settle();
        Type("acetylcysteine");
        Press(Key.Enter);
        var review = (ReviewView)CurrentScreen!;
        Assert.Contains("acetylcysteine", Texts(review));
        Assert.Contains("ANSWER", Texts(review));
        Picture(Window, "09-review-typed");
    }

    [AvaloniaFact]
    public void AllCaughtUp()
    {
        Pharmacology = Db.AddDeck("Pharmacology");
        Window.Open(new ReviewView(Pharmacology));
        Settle();
        Assert.Contains("No cards yet", Texts(CurrentScreen!));
        Picture(Window, "10-review-empty");
    }

    [AvaloniaFact]
    public void AddingCardsInTheEditor()
    {
        AddSampleDecks();
        var deckView = new DeckView(Pharmacology);
        Window.Open(deckView);
        Settle();
        Press(Key.N, KeyModifiers.Control);
        var editor = Opened<CardEditorWindow>();
        Assert.NotNull(editor);
        Find<RadioButton>(editor!, "Basic").IsChecked ??= true;
        All<RadioButton>(editor!).First(r => (string?)r.Tag == "Basic").IsChecked = true;
        Settle();
        Type("Normal canine heart rate?", editor);
        Find<CardTextBox>(editor!, "Back").Focus();
        Settle();
        Type("60–160 bpm", editor);
        Picture(editor!, "11-editor");
        int before = Db.GetCards(Pharmacology.Id).Count;
        Press(Key.Enter, KeyModifiers.Control, editor);   // Save & next
        Assert.Equal(before + 1, Db.GetCards(Pharmacology.Id).Count);
        Assert.Contains("1 card added so far", Texts(editor!));
        Assert.Equal("", Find<CardTextBox>(editor!, "Front").Text);
    }

    [AvaloniaFact]
    public void BoldAndItalicShowAsYouTypeWithTheStarsHidden()
    {
        AddSampleDecks();
        var editor = new CardEditorWindow(Parasitology, null);
        editor.Show(Window);
        Settle();
        var front = Find<CardTextBox>(editor, "Front");
        front.Focus();
        Type("Toxocara canis eggs", editor);
        front.Select(0, 14);
        Press(Key.I, KeyModifiers.Control, editor);
        Assert.Equal("*Toxocara canis* eggs", front.Text);
        front.Select(17, 4);
        Press(Key.B, KeyModifiers.Control, editor);
        Assert.Equal("*Toxocara canis* **eggs**", front.Text);

        // The arrow keys step over the hidden stars: one press, one letter you can see.
        front.Select(0, 0);
        Press(Key.Right, KeyModifiers.None, editor);
        Type("X", editor);
        Assert.Equal("*TXoxocara canis* **eggs**", front.Text);   // typed inside the italic

        // Backspace at the very end takes the last letter, not a hidden star, and an emptied word loses its stars.
        front.Select(front.Text.Length, 0);
        for (int i = 0; i < 4; i++) Press(Key.Back, KeyModifiers.None, editor);
        Assert.Equal("*TXoxocara canis* ", front.Text);

        // Selecting the words with their (hidden) stars and pressing Italic takes the italic off.
        front.Select(0, 17);
        Press(Key.I, KeyModifiers.Control, editor);
        Assert.Equal("TXoxocara canis ", front.Text);

        front.Text = "*Toxocara canis* eggs are **very** sticky";
        Settle();
        Picture(editor, "12a-editor-formatting");
    }

    [AvaloniaFact]
    public void ClozeCardsWithTheHideHelper()
    {
        AddSampleDecks();
        var editor = new CardEditorWindow(Parasitology, null);
        editor.Show(Window);
        Settle();
        All<RadioButton>(editor).First(r => (string?)r.Tag == "Cloze").IsChecked = true;
        Settle();
        Find<CardTextBox>(editor, "Front").Text = "Toxocara canis eggs need weeks to embryonate";
        Find<TextBox>(editor, "Word to hide").Text = "weeks";
        Settle();
        Click(All<Button>(editor).First(b => b.Content as string == "Hide c1"));
        Assert.Equal("Toxocara canis eggs need {{c1::weeks}} to embryonate", Find<CardTextBox>(editor, "Front").Text);
        Assert.Contains("This makes 1 card.", Texts(editor));
        Picture(editor, "12-editor-cloze");
    }

    [AvaloniaFact]
    public void EditingACardFromTheReview()
    {
        AddSampleDecks();
        Window.Open(new ReviewView(Pharmacology));
        Settle();
        Press(Key.E);
        var editor = Opened<CardEditorWindow>();
        Assert.NotNull(editor);
        Assert.Contains("Edit card", Texts(editor!));
        Picture(editor!, "13-editor-edit");
    }

    [AvaloniaFact]
    public void PastingManyCards()
    {
        AddSampleDecks();
        var paste = new PasteManyWindow(Pharmacology);
        paste.Show(Window);
        Settle();
        Find<TextBox>(paste, "Cards to paste").Text = "Ketamine | NMDA antagonist\nXylazine\tAlpha-2 agonist";
        Settle();
        var add = All<Button>(paste).First(b => b.Content is string s && s.StartsWith("Add"));
        Assert.Equal("Add 2 cards", add.Content);
        Picture(paste, "14-paste-many");
        int before = Db.GetCards(Pharmacology.Id).Count;
        Click(add);
        Assert.Equal(before + 2, Db.GetCards(Pharmacology.Id).Count);
    }

    [AvaloniaFact]
    public void CustomStudyPicksCardsAndStartsASession()
    {
        AddSampleDecks();
        Db.SaveSettings(Db.GetSettings() with { NewCardsPerDay = 0 });
        var window = new CustomStudyWindow(Pharmacology);
        window.Show(Window);
        Settle();
        var start = All<Button>(window).First(b => b is not ToggleButton && b.Content is string s && s.StartsWith("Study"));
        Assert.Equal("Study 4 cards", start.Content);   // more new cards: past the limit of 0

        All<RadioButton>(window).First(rb => rb.Tag as string == "ByTag").IsChecked = true;
        All<ToggleButton>(window).First(tb => tb.Content as string == "nsaid").IsChecked = true;
        Settle();
        Assert.Equal("Study 1 card", start.Content);
        Picture(window, "19-custom-study");

        All<RadioButton>(window).First(rb => rb.Tag as string == "ReviewAhead").IsChecked = true;
        Settle();
        Assert.False(start.IsEnabled);   // nothing studied yet, so nothing comes due

        var review = new ReviewView(Pharmacology, new Rev.Core.Review.CustomStudy { Limit = 1 });
        Window.Open(review);
        Settle();
        Assert.Contains("More new cards", Texts(review));
        Assert.Contains("1 new", Texts(review));
    }

    [AvaloniaFact]
    public void CustomStudyInADeckWithNothingNewStartsOnStudyByTagAndExplains()
    {
        AddSampleDecks();
        foreach (var card in Db.GetCards(Parasitology.Id))   // everything learned, next due in 3 weeks
        {
            card.SchedulingState = card.SchedulingState with
            {
                Phase = CardPhase.Review, IntervalDays = 21, Reps = 3,
                Due = DateTime.Now.AddDays(21), LastReviewed = DateTime.Now.AddDays(-1),
            };
            Db.UpdateCard(card);
        }
        var window = new CustomStudyWindow(Parasitology);
        window.Show(Window);
        Settle();

        Assert.True(All<RadioButton>(window).First(rb => rb.Tag as string == "ByTag").IsChecked);
        var start = All<Button>(window).First(b => b is not ToggleButton && b.Content is string s && s.StartsWith("Study"));
        Assert.Equal("Study 3 cards", start.Content);
        Assert.Contains("0", Texts(All<RadioButton>(window).First(rb => rb.Tag as string == "MoreNew")));

        All<RadioButton>(window).First(rb => rb.Tag as string == "MoreNew").IsChecked = true;
        Settle();
        Assert.Contains("There are no new cards left in this deck", Texts(window));
        Picture(window, "20-custom-study-nothing-new");

        All<RadioButton>(window).First(rb => rb.Tag as string == "ReviewAhead").IsChecked = true;
        Settle();
        Assert.Contains("The next card is due in", Texts(window));
    }

    [AvaloniaFact]
    public void Stats()
    {
        AddSampleDecks();
        _ = Window;
        Press(Key.D3, KeyModifiers.Control);
        string text = Texts(Assert.IsType<StatsView>(CurrentScreen));
        Assert.Contains("Your progress", text);
        Assert.Contains("Last 7 days", text);
        Assert.Contains("Today", text);
        Picture(Window, "15-stats");
    }

    [AvaloniaFact]
    public void SettingsAreSavedAtOnce()
    {
        AddSampleDecks();
        _ = Window;
        Press(Key.OemComma, KeyModifiers.Control);
        var settings = Assert.IsType<SettingsView>(CurrentScreen);
        foreach (var section in new[] { "Studying", "Sync with Anki", "Anki files", "Backups", "About" })
            Assert.Contains(section, Texts(settings).Split(" | "));
        Find<TextBox>(settings, "Your name").Text = "Marshal";
        Find<TextBox>(settings, "New cards per day").Text = "35";
        Find<RadioButton>(settings, "SM-2 (classic)").IsChecked = true;
        Settle();
        var saved = Db.GetSettings();
        Assert.Equal(("Marshal", 35, SchedulerKind.Sm2), (saved.UserName, saved.NewCardsPerDay, saved.Scheduler));
        Assert.DoesNotContain("Target memory", Texts(settings));   // only for FSRS
        Find<RadioButton>(settings, "FSRS (recommended)").IsChecked = true;
        Settle();
        Assert.Contains("Target memory: 90%", Texts(settings));
        Assert.Equal(SchedulerKind.Fsrs, Db.GetSettings().Scheduler);
        Assert.Contains("Not set up yet", Texts(settings));
        Assert.Contains(Core.Data.RevDatabase.DefaultFolder, Texts(settings));
        Picture(Window, "16-settings");
    }

    [AvaloniaFact]
    public void DarkMode()
    {
        AddSampleDecks();
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        try
        {
            _ = Window;
            Picture(Window, "17-home-dark");
            Window.Open(new ReviewView(Pharmacology));
            Settle();
            Press(Key.Space);
            Picture(Window, "18-review-dark");
        }
        finally
        {
            Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        }
    }
}
