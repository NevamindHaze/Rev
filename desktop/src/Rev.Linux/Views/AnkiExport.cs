using Avalonia;
using Rev.Core.Anki;
using Rev.Core.Media;
using Rev.Core.Models;
using Rev.Core.Text;
using Rev.Linux.Controls;

namespace Rev.Linux.Views;

/// <summary>Saving decks as an Anki .apkg file: asks where, writes it in the background, then says what it did.</summary>
public static class AnkiExport
{
    /// <param name="decks">The decks to export, with their cards.</param>
    /// <param name="fileName">The suggested file name, e.g. the deck's name.</param>
    public static async Task Run(Visual owner, List<(Deck Deck, List<Card> Cards)> decks, string fileName)
    {
        if (decks.Sum(d => d.Cards.Count) == 0)
        {
            await RevDialog.Tell(owner, "Nothing to export", "There are no cards to export yet.");
            return;
        }

        var path = await FileDialogs.SaveFile(owner, "Export to Anki", SafeFileName(fileName) + ".apkg", "Anki deck package", "apkg");
        if (path is null) return;

        string media = MediaStore.Folder;
        try
        {
            ExportResult result;
            using (FileDialogs.Busy(owner)) result = await Task.Run(() => ApkgWriter.Write(path, decks, media));
            string images = result.Images > 0 ? $" and {result.Images.Counted("image")}" : "";
            await RevDialog.Tell(owner, "Exported to Anki",
                $"{result.Decks.Counted("deck")} with {result.Cards.Counted("card")}{images} saved to {path}. " +
                "Open it with Anki (File → Import) to add it there.");
        }
        catch (Exception error)
        {
            await RevDialog.Tell(owner, "The export didn't work", error.Message);
        }
    }

    /// <summary>A deck name without characters file names can't have (e.g. "Vet › Pharm" stays readable).</summary>
    private static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        string safe = new string(name.Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
        return safe.Length == 0 ? "Rev" : safe;
    }
}
