using System.Text.Json;
using Rev.Core.Data;
using Rev.Core.Media;
using Rev.Core.Text;

namespace Rev.Core.Sync;

/// <summary>Reading and writing files in the shared folder.</summary>
public static class SyncFiles
{
    /// <summary>Writes a file in one go (a temporary file, then a rename), so no app ever reads half of it.</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// One sync with Anki through the shared "Rev Sync" folder, the way Rev on iPhone does it:
/// 1. read anki.json (written by the Rev Sync add-on) and apply what's newer;
/// 2. write windows.json with everything Rev for Windows has (only if something changed);
/// 3. copy pictures in and out of the folder's media/ subfolder.
/// The add-on does the rest on Anki's side (and passes changes on to Rev on iPhone).
/// </summary>
public sealed class SyncService(RevDatabase db, string folder, string statePath, string mediaFolder)
{
    public const string AnkiFile = "anki.json";
    public const string OwnFile = "windows.json";

    private string AnkiPath => Path.Combine(folder, AnkiFile);
    private string OwnPath => Path.Combine(folder, OwnFile);
    private string SharedMedia => Path.Combine(folder, "media");

    /// <summary>What a sync would do, without changing anything (for the first sync's preview).</summary>
    public (SyncReport FromAnki, int CardsToSend) Preview(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var state = SyncState.Load(statePath);
        var anki = ReadAnki();
        var report = anki is null ? new SyncReport() : db.DryRun(() => SyncMerger.Apply(db, anki, state.Copy(), at));
        return (report, db.GetCards().Count);
    }

    /// <summary>Whether Anki's file is in the folder yet (it appears after the add-on's first sync).</summary>
    public bool AnkiFileExists => File.Exists(AnkiPath);

    /// <summary>Runs one sync. Throws if the folder can't be read or written.</summary>
    public SyncReport Run(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"The sync folder {folder} doesn't exist.");
        var state = SyncState.Load(statePath);
        var report = new SyncReport();

        // 1. Apply what changed in Anki.
        if (ReadAnki() is { } anki && anki.WrittenAt != state.LastAnkiWrittenAt)
        {
            report = SyncMerger.Apply(db, anki, state, at);
            CopyImagesIn(anki.Cards.SelectMany(c => CardText.ImageNames(Anki.AnkiText.PlainText(c.Front))
                .Concat(CardText.ImageNames(Anki.AnkiText.PlainText(c.Back)))));
            state.LastAnkiWrittenAt = anki.WrittenAt;
        }

        // 2. Write Rev's side (only if something changed).
        var snapshot = SyncMerger.MakeSnapshot(db, state, at);
        string digest = SyncMerger.Digest(snapshot);
        if (digest != state.LastExportDigest || !File.Exists(OwnPath))
        {
            snapshot.WrittenAt = SyncMerger.Seconds(at);
            SyncFiles.WriteAtomic(OwnPath, JsonSerializer.SerializeToUtf8Bytes(snapshot));
            state.LastExportDigest = digest;
        }
        CopyImagesOut(snapshot.Cards.SelectMany(c => CardText.ImageNames(c.Front).Concat(CardText.ImageNames(c.Back))));

        state.Save(statePath);
        return report;
    }

    private SyncSnapshot? ReadAnki()
    {
        if (!File.Exists(AnkiPath)) return null;   // the add-on hasn't written it yet
        var snapshot = JsonSerializer.Deserialize<SyncSnapshot>(File.ReadAllBytes(AnkiPath))
                       ?? throw new InvalidDataException("anki.json is empty.");
        return snapshot.Format == 1 ? snapshot : throw new InvalidDataException(
            "anki.json was written by a newer Rev Sync add-on. Please update Rev for Windows.");
    }

    /// <summary>Copies pictures the cards need from the shared folder into Rev, if Rev doesn't have them.</summary>
    private void CopyImagesIn(IEnumerable<string> names)
    {
        foreach (var name in names.Distinct())
        {
            if (MediaStore.SafeName(name) is not { } safe) continue;
            string source = Path.Combine(SharedMedia, safe), target = Path.Combine(mediaFolder, safe);
            if (File.Exists(source) && !File.Exists(target))
            {
                Directory.CreateDirectory(mediaFolder);
                File.Copy(source, target);
            }
        }
    }

    /// <summary>Copies Rev's pictures into the shared folder, so Anki (and the iPhone) can use them.</summary>
    private void CopyImagesOut(IEnumerable<string> names)
    {
        foreach (var name in names.Distinct())
        {
            if (MediaStore.SafeName(name) is not { } safe) continue;
            string source = Path.Combine(mediaFolder, safe), target = Path.Combine(SharedMedia, safe);
            if (File.Exists(source) && !File.Exists(target))
            {
                Directory.CreateDirectory(SharedMedia);
                File.Copy(source, target);
            }
        }
    }

    /// <summary>
    /// The add-on's folder setting, to suggest the same folder. Anki keeps it in its data folder under
    /// addons21/rev_sync, in meta.json (if you changed it) or the add-on's config.json.
    /// </summary>
    public static string? FolderFromAnkiAddon()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string anki in AnkiDataFolders(home, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                Environment.GetEnvironmentVariable("XDG_DATA_HOME"), OperatingSystem.IsWindows()))
        {
            string addon = Path.Combine(anki, "addons21", "rev_sync");
            foreach (var (file, inConfig) in new[] { ("meta.json", true), ("config.json", false) })
            {
                try
                {
                    string path = Path.Combine(addon, file);
                    if (!File.Exists(path)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    var root = doc.RootElement;
                    if (inConfig && !root.TryGetProperty("config", out root)) continue;
                    if (root.TryGetProperty("sync_folder", out var value) && value.GetString() is { Length: > 0 } folder
                        && ExpandPath(folder, home, OperatingSystem.IsWindows()) is { } expanded)
                        return expanded;
                }
                catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { }
            }
        }
        return null;
    }

    /// <summary>
    /// Where Anki keeps its data: %APPDATA%\Anki2 on Windows; on Linux ~/.local/share/Anki2 (or under
    /// $XDG_DATA_HOME), or the Flatpak's own folder if Anki was installed from Flathub.
    /// </summary>
    public static IEnumerable<string> AnkiDataFolders(string home, string appData, string? xdgDataHome, bool windows)
    {
        if (windows)
        {
            yield return Path.Combine(appData, "Anki2");
            yield break;
        }
        yield return Path.Combine(xdgDataHome is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".local", "share"), "Anki2");
        yield return Path.Combine(home, ".var", "app", "net.ankiweb.Anki", "data", "Anki2");
        yield return Path.Combine(home, "Library", "Application Support", "Anki2");   // a Mac
    }

    /// <summary>
    /// A folder as the add-on's setting writes it, as a real path: "%USERPROFILE%\iCloudDrive\Rev Sync" on
    /// Windows; "~/Rev Sync" or "$HOME/Rev Sync" on Linux. Null for a Windows-style setting on Linux (the
    /// add-on's original default), which can't be the folder there.
    /// </summary>
    public static string? ExpandPath(string folder, string home, bool windows)
    {
        if (windows) return Environment.ExpandEnvironmentVariables(folder);
        string path = folder.Trim();
        if (path == "~" || path.StartsWith("~/")) path = home + path[1..];
        path = path.Replace("${HOME}", home).Replace("$HOME", home);
        return path.Contains('%') || path.Contains('\\') || !Path.IsPathRooted(path) ? null : path;
    }

    /// <summary>
    /// The folder to suggest when nothing is set up: where iCloud for Windows keeps "Rev Sync", or on Linux
    /// (which has no iCloud) a "Rev Sync" folder in your home folder.
    /// </summary>
    public static string UsualFolder => UsualFolderFor(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), OperatingSystem.IsWindows());

    public static string UsualFolderFor(string home, bool windows) =>
        windows ? Path.Combine(home, "iCloudDrive", "Rev Sync") : Path.Combine(home, "Rev Sync");
}
