namespace Rev.Core.Sync;

/// <summary>
/// Notices when the Rev Sync add-on rewrites anki.json in the shared folder, so Rev for Windows can sync
/// within seconds instead of waiting for its 10-minute timer. Anki runs on the same computer, so the file
/// changes on this disk right away; that's how a change made on the iPhone (over Wi-Fi to Anki) reaches
/// Rev for Windows quickly.
///
/// The add-on writes the file in two steps (a temporary file, then a rename), and Windows reports several
/// events for that. So <see cref="AnkiFileChanged"/> is raised once, a moment after the last of them.
/// It's raised on a background thread.
/// </summary>
public sealed class AnkiFileWatcher : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly Timer settle;
    private readonly TimeSpan delay;
    private bool disposed;

    public event EventHandler? AnkiFileChanged;

    /// <param name="folder">The shared "Rev Sync" folder. Throws if it doesn't exist.</param>
    /// <param name="delay">How long to wait after the last event (2 seconds unless a test says otherwise).</param>
    public AnkiFileWatcher(string folder, TimeSpan? delay = null)
    {
        this.delay = delay ?? TimeSpan.FromSeconds(2);
        settle = new Timer(_ =>
        {
            if (!disposed) AnkiFileChanged?.Invoke(this, EventArgs.Empty);
        });
        watcher = new FileSystemWatcher(folder, "anki.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        watcher.Changed += (_, _) => Poke();
        watcher.Created += (_, _) => Poke();
        watcher.Renamed += (_, _) => Poke();
        // Too many changes at once: Windows lost some events, so check anyway.
        watcher.Error += (_, _) => Poke();
        watcher.EnableRaisingEvents = true;
    }

    /// <summary>(Re)starts the short wait; the event is raised when it ends without another change.</summary>
    private void Poke()
    {
        if (!disposed) settle.Change(delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        disposed = true;
        watcher.Dispose();
        settle.Dispose();
    }
}
