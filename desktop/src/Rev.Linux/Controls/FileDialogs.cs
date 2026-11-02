using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Rev.Linux.Controls;

/// <summary>
/// The desktop's own "Open", "Save as" and "Choose folder" windows (on Linux Mint, the usual file chooser).
/// Each returns the chosen path, or null if you cancelled.
/// </summary>
public static class FileDialogs
{
    private static IStorageProvider? Storage(Visual owner) => TopLevel.GetTopLevel(owner)?.StorageProvider;

    private static string Documents
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Directory.Exists(documents) ? documents : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    private static async Task<IStorageFolder?> Folder(IStorageProvider storage, string? path) =>
        path != null && Directory.Exists(path) ? await storage.TryGetFolderFromPathAsync(path) : null;

    public static async Task<string?> OpenFile(Visual owner, string title, string typeName, params string[] patterns)
    {
        if (Storage(owner) is not { } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await Folder(storage, Documents),
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = patterns }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> SaveFile(Visual owner, string title, string suggestedName, string typeName, string extension)
    {
        if (Storage(owner) is not { } storage) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await Folder(storage, Documents),
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = ["*." + extension] }],
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> ChooseFolder(Visual owner, string title, string? start)
    {
        if (Storage(owner) is not { } storage) return null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await Folder(storage, start) ?? await Folder(storage, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>Shows the busy (wait) cursor over the window until the returned object is disposed.</summary>
    public static IDisposable Busy(Visual owner)
    {
        var top = TopLevel.GetTopLevel(owner);
        var before = top?.Cursor;
        if (top != null) top.Cursor = new Cursor(StandardCursorType.Wait);
        return new Restore(() => { if (top != null) top.Cursor = before; });
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
