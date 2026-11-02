using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Rev.App.Controls;
using Rev.Core.Anki;
using Rev.Core.Media;
using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.App.Views;

/// <summary>Saving decks as an Anki .apkg file: asks where, writes it in the background, then says what it did.</summary>
public static class AnkiExport
{
    /// <param name="decks">The decks to export, with their cards.</param>
    /// <param name="fileName">The suggested file name, e.g. the deck's name.</param>
    public static async Task Run(Window? owner, List<(Deck Deck, List<Card> Cards)> decks, string fileName)
    {
        if (decks.Sum(d => d.Cards.Count) == 0)
        {
            RevDialog.Tell(owner, "Nothing to export", "There are no cards to export yet.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export to Anki",
            FileName = SafeFileName(fileName) + ".apkg",
            DefaultExt = ".apkg",
            Filter = "Anki deck package (*.apkg)|*.apkg",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(owner) != true) return;

        string path = dialog.FileName;
        string media = MediaStore.Folder;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var result = await Task.Run(() => ApkgWriter.Write(path, decks, media));
            Mouse.OverrideCursor = null;
            string images = result.Images > 0 ? $" and {result.Images.Counted("image")}" : "";
            RevDialog.Tell(owner, "Exported to Anki",
                $"{result.Decks.Counted("deck")} with {result.Cards.Counted("card")}{images} saved to {path}. " +
                "Open it with Anki (File → Import) to add it there.");
        }
        catch (Exception error)
        {
            Mouse.OverrideCursor = null;
            RevDialog.Tell(owner, "The export didn't work", error.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>A deck name without characters Windows doesn't allow in file names (e.g. "Vet › Pharm" stays readable).</summary>
    private static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        string safe = new string(name.Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
        return safe.Length == 0 ? "Rev" : safe;
    }
}
