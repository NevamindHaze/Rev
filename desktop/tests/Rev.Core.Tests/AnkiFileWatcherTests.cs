using Rev.Core.Sync;

namespace Rev.Core.Tests;

/// <summary>Noticing that Anki rewrote anki.json, the way the Rev Sync add-on writes it.</summary>
public sealed class AnkiFileWatcherTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-watch-" + Guid.NewGuid().ToString("N"));
    private readonly AnkiFileWatcher watcher;
    private int raised;

    public AnkiFileWatcherTests()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "anki.json"), "{}");
        watcher = new AnkiFileWatcher(folder, TimeSpan.FromMilliseconds(300));
        watcher.AnkiFileChanged += (_, _) => Interlocked.Increment(ref raised);
    }

    public void Dispose()
    {
        watcher.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private string Path_(string name) => Path.Combine(folder, name);

    /// <summary>Like the add-on (Python's os.replace): write a temporary file, then rename it over anki.json.</summary>
    private void WriteLikeTheAddOn(string content)
    {
        File.WriteAllText(Path_("anki.json.tmp"), content);
        File.Move(Path_("anki.json.tmp"), Path_("anki.json"), overwrite: true);
    }

    /// <summary>How many times the event was raised, after giving it time to arrive.</summary>
    private int RaisedAfterWaiting()
    {
        var until = DateTime.Now.AddSeconds(5);
        while (Volatile.Read(ref raised) == 0 && DateTime.Now < until) Thread.Sleep(50);
        Thread.Sleep(800);   // any extra (unwanted) events would arrive by now
        return Volatile.Read(ref raised);
    }

    [Fact]
    public void ARewriteByTheAddOnIsNoticedOnce()
    {
        WriteLikeTheAddOn("{\"writtenAt\": 1}");
        Assert.Equal(1, RaisedAfterWaiting());
    }

    [Fact]
    public void APlainOverwriteIsNoticedToo()
    {
        File.WriteAllText(Path_("anki.json"), "{\"writtenAt\": 2}");
        Assert.Equal(1, RaisedAfterWaiting());
    }

    [Fact]
    public void SeveralQuickRewritesGiveOneSync()
    {
        for (int i = 0; i < 5; i++)
        {
            WriteLikeTheAddOn($"{{\"writtenAt\": {i}}}");
            Thread.Sleep(50);
        }
        Assert.Equal(1, RaisedAfterWaiting());
    }

    [Fact]
    public void OtherFilesInTheFolderAreIgnored()
    {
        // Rev for Windows' own file, the iPhone's, and pictures: none of them should start a sync.
        File.WriteAllText(Path_("windows.json"), "{}");
        File.WriteAllText(Path_("rev.json"), "{}");
        Directory.CreateDirectory(Path_("media"));
        File.WriteAllText(Path.Combine(folder, "media", "anki.json"), "{}");
        Assert.Equal(0, RaisedAfterWaiting());
    }

    [Fact]
    public void NothingIsRaisedAfterItStops()
    {
        watcher.Dispose();
        WriteLikeTheAddOn("{\"writtenAt\": 3}");
        Assert.Equal(0, RaisedAfterWaiting());
    }

    [Fact]
    public void AFolderThatDoesntExistIsRefused()
    {
        Assert.ThrowsAny<ArgumentException>(() => new AnkiFileWatcher(Path_("missing")));
    }
}
