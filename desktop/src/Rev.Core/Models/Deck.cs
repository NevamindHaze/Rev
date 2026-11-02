namespace Rev.Core.Models;

/// <summary>A deck is a named group of cards, e.g. "Pharmacology" or "Large Animal Surgery".</summary>
public sealed class Deck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
