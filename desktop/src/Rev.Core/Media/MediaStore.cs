using Rev.Core.Data;

namespace Rev.Core.Media;

/// <summary>
/// Card images live in a "Media" folder next to the database.
/// Card text refers to an image with a marker like [img:heart.jpg].
/// </summary>
public static class MediaStore
{
    public static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff" };

    public static string Folder
    {
        get
        {
            string folder = Path.Combine(RevDatabase.DefaultFolder, "Media");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Only a plain file name is allowed (no folders like "..\"), for safety.</summary>
    public static string? SafeName(string name)
    {
        string last = Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
        return last.Length == 0 || last.StartsWith('.') ? null : last;
    }

    public static bool IsImage(string name) => ImageExtensions.Contains(Path.GetExtension(name));

    /// <summary>Where an image is stored, if we have it.</summary>
    public static string? PathFor(string name)
    {
        if (SafeName(name) is not { } safe) return null;
        string path = Path.Combine(Folder, safe);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Saves an image file. Returns false if the name isn't usable.</summary>
    public static bool Save(byte[] data, string name)
    {
        if (SafeName(name) is not { } safe || !IsImage(safe)) return false;
        File.WriteAllBytes(Path.Combine(Folder, safe), data);
        return true;
    }
}
