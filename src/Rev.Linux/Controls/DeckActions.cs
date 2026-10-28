using Avalonia;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;
using Rev.Linux.Views;

namespace Rev.Linux.Controls;

/// <summary>
/// New deck, Rename, Delete and Export to Anki, the same from the sidebar, Home and a deck's page.
/// Each asks with a dialog, then updates the sidebar and the page you're on.
/// </summary>
public static class DeckActions
{
    /// <summary>Asks for a name, makes the deck and opens it. Null if you cancelled.</summary>
    public static async Task<Deck?> New(Visual owner)
    {
        var name = await RevDialog.Prompt(owner, "New deck", null, "", "Create", "Deck name");
        if (name is null) return null;
        var deck = App.Database.AddDeck(name);
        MainWindow.Current.RefreshSidebar();
        MainWindow.Current.ShowDeck(deck);
        return deck;
    }

    public static async Task Rename(Visual owner, Deck deck)
    {
        var name = await RevDialog.Prompt(owner, "Rename deck", null, deck.Name, "Save", "Deck name");
        if (name is null || name == deck.Name) return;
        App.Database.RenameDeck(deck.Id, name);
        Updated();
    }

    /// <summary>Asks first (the keyboard starts on Cancel). Deleting the deck you have open goes Home.</summary>
    public static async Task Delete(Visual owner, Deck deck)
    {
        int count = App.Database.CountCards(deck.Id);
        bool sure = await RevDialog.Confirm(owner,
            $"Delete “{deck.Name}”?",
            $"This permanently deletes the deck and its {count.Counted("card")}, including their study progress. " +
            "This can't be undone.",
            "Delete", destructive: true);
        if (!sure) return;
        App.Database.DeleteDeck(deck.Id);
        bool wasOpen = MainWindow.Current.CurrentScreen switch
        {
            DeckView page => page.Deck.Id == deck.Id,
            ReviewView review => review.Deck?.Id == deck.Id,
            _ => false,
        };
        if (wasOpen) MainWindow.Current.ShowHome();
        Updated();
    }

    public static async Task Export(Visual owner, Deck deck)
    {
        var current = App.Database.GetDecks().FirstOrDefault(d => d.Id == deck.Id) ?? deck;
        var cards = App.Database.GetCards(deck.Id).OrderBy(c => c.CreatedAt).ToList();
        await AnkiExport.Run(owner, [(current, cards)], current.Name);
    }

    private static void Updated()
    {
        MainWindow.Current.RefreshSidebar();
        if (MainWindow.Current.CurrentScreen is IScreen page) page.Refresh();
    }
}
