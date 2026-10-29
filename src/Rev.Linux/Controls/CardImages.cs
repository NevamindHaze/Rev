using Avalonia.Media.Imaging;
using Rev.Core.Media;

namespace Rev.Linux.Controls;

/// <summary>
/// Puts a picture you add to a card into Rev's pictures folder (MediaStore), under a name no other picture has,
/// so the card can show it with [img:name]. Sync copies it to Anki and the iPhone with the card.
/// </summary>
public static class CardImages
{
    /// <summary>The marker card text uses for a picture.</summary>
    public static string Marker(string name) => $"[img:{name}]";

    /// <summary>Copies an image file in. Null if the file isn't a picture Rev can show.</summary>
    public static string? AddFile(string path)
    {
        if (MediaStore.SafeName(Path.GetFileName(path)) is not { } name || !MediaStore.IsImage(name)) return null;
        string unique = UniqueName(name);
        File.Copy(path, Path.Combine(MediaStore.Folder, unique));
        return unique;
    }

    /// <summary>Saves a pasted picture (e.g. a screenshot) as a PNG.</summary>
    public static string AddBitmap(Bitmap bitmap)
    {
        string name = UniqueName($"pasted-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using var file = File.Create(Path.Combine(MediaStore.Folder, name));
        bitmap.Save(file);
        return name;
    }

    /// <summary>
    /// The name without spaces (they'd break the picture in Anki's HTML), and with -2, -3… added if a picture
    /// of that name is already there, so adding one never replaces another card's picture.
    /// </summary>
    private static string UniqueName(string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name).Replace(' ', '-');
        string extension = Path.GetExtension(name).ToLowerInvariant();
        string candidate = stem + extension;
        for (int n = 2; MediaStore.PathFor(candidate) != null; n++) candidate = $"{stem}-{n}{extension}";
        return candidate;
    }
}
