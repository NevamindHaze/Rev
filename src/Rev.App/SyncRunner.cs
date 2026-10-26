using System.IO;
using System.Windows;
using System.Windows.Threading;
using Rev.App.Controls;
using Rev.App.Views;
using Rev.Core.Backup;
using Rev.Core.Data;
using Rev.Core.Media;
using Rev.Core.Sync;

namespace Rev.App;

/// <summary>
/// When and how Rev for Windows syncs with Anki (through the Rev Sync add-on and the shared folder).
/// Sync stays off until you choose the folder and agree to the first sync in Settings. After that it runs
/// when Rev opens, when it closes, and every 10 minutes (never in the middle of a review).
/// </summary>
public static class SyncRunner
{
    private const string FolderKey = "syncFolder";
    private const string ReadyKey = "syncReady";
    private const string LastTimeKey = "syncLastTime";
    private const string LastMessageKey = "syncLastMessage";

    private static DispatcherTimer? timer;
    private static bool running;

    /// <summary>The shared folder, or null when sync isn't set up.</summary>
    public static string? Folder => App.Database.GetPreference(FolderKey) is { Length: > 0 } folder ? folder : null;

    /// <summary>True once you've agreed to the first sync.</summary>
    public static bool IsOn => Folder != null && App.Database.GetPreference(ReadyKey) == "yes";

    public static string? LastMessage => App.Database.GetPreference(LastMessageKey);
    public static DateTime? LastTime =>
        long.TryParse(App.Database.GetPreference(LastTimeKey), out long ms) ? RevDatabase.FromMs(ms) : null;

    /// <summary>Fired after a sync that changed something, so the screen can refresh.</summary>
    public static event EventHandler? Synced;

    private static string StatePath => Path.Combine(RevDatabase.DefaultFolder, "sync-state.json");

    private static SyncService Service(string folder) => new(App.Database, folder, StatePath, MediaStore.Folder);

    /// <summary>Starts the automatic syncs (call once, when the main window is shown).</summary>
    public static void Start()
    {
        timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        timer.Tick += (_, _) => RunQuietly();
        timer.Start();
        // A few seconds after opening, so the window appears first.
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        first.Tick += (_, _) => { first.Stop(); RunQuietly(); };
        first.Start();
    }

    /// <summary>An automatic sync: does nothing unless sync is on, never during a review, and never shows errors
    /// (the last result is shown in Settings instead).</summary>
    public static void RunQuietly()
    {
        if (!IsOn || running) return;
        if (Application.Current.MainWindow is MainWindow { IsReviewing: true }) return;
        Run(out _);
    }

    /// <summary>Runs a sync now. Returns false (with the reason) if it didn't work.</summary>
    public static bool Run(out string message)
    {
        message = "";
        if (Folder is not { } folder || running) return false;
        running = true;
        try
        {
            var report = Service(folder).Run();
            message = report.Summary;
            Remember(message);
            if (report.Any) Synced?.Invoke(null, EventArgs.Empty);
            return true;
        }
        catch (Exception error)
        {
            message = $"Sync didn't finish: {error.Message}";
            App.Database.SetPreference(LastMessageKey, message);
            return false;
        }
        finally
        {
            running = false;
        }
    }

    private static void Remember(string message)
    {
        App.Database.SetPreference(LastTimeKey, RevDatabase.ToMs(DateTime.Now).ToString());
        App.Database.SetPreference(LastMessageKey, message);
    }

    /// <summary>
    /// Sets up sync with a folder you chose: saves a backup of Rev, shows what the first sync would do, and
    /// syncs only if you agree. Returns true if sync is now on.
    /// </summary>
    public static bool SetUp(Window? owner, string folder)
    {
        var service = Service(folder);
        if (!service.AnkiFileExists)
        {
            RevDialog.Tell(owner, "Anki hasn't written its file yet",
                $"There's no anki.json in {folder} yet. Make sure the Rev Sync add-on (with Windows support) is " +
                "installed in Anki and its folder is set to this one, then open Anki once and choose " +
                "Tools → Rev Sync now. Then click Sync now here.");
            App.Database.SetPreference(FolderKey, folder);
            App.Database.SetPreference(ReadyKey, "no");
            return false;
        }

        (SyncReport FromAnki, int CardsToSend) preview;
        string backup;
        try
        {
            preview = service.Preview();
            // A backup of Rev first, in case anything goes wrong.
            string backups = Path.Combine(RevDatabase.DefaultFolder, "Backups");
            Directory.CreateDirectory(backups);
            backup = Path.Combine(backups, $"Before first sync {DateTime.Now:yyyy-MM-dd HHmmss}.json");
            BackupService.Save(BackupService.Make(App.Database), backup);
        }
        catch (Exception error)
        {
            RevDialog.Tell(owner, "Sync can't start", error.Message);
            return false;
        }

        bool go = RevDialog.Confirm(owner, "Sync with Anki for the first time?",
            $"From Anki: {preview.FromAnki.Summary} Rev will then send its {Core.Text.Plural.Counted(preview.CardsToSend, "card")} " +
            "to Anki, where the Rev Sync add-on adds or updates them. A backup of Rev was saved first, in the Backups folder.",
            "Sync");
        App.Database.SetPreference(FolderKey, folder);
        if (!go)
        {
            App.Database.SetPreference(ReadyKey, "no");
            return false;
        }
        App.Database.SetPreference(ReadyKey, "yes");
        bool ok = Run(out string message);
        RevDialog.Tell(owner, ok ? "Synced with Anki" : "Sync didn't finish", message);
        return ok;
    }

    /// <summary>Stops syncing. (The shared folder is left as it is; delete windows.json there to remove Rev for Windows from the sync.)</summary>
    public static void TurnOff()
    {
        App.Database.SetPreference(FolderKey, "");
        App.Database.SetPreference(ReadyKey, "no");
        App.Database.SetPreference(LastMessageKey, "");
    }

    /// <summary>The folder to suggest: the one the Rev Sync add-on uses, or iCloud Drive's usual place.</summary>
    public static string SuggestedFolder =>
        Folder ?? SyncService.FolderFromAnkiAddon() ?? SyncService.UsualFolder;
}
