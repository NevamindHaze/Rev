using Rev.Core.Sync;

namespace Rev.Core.Tests;

/// <summary>Finding the add-on's sync folder on Windows and on Linux (where there's no iCloud Drive).</summary>
public sealed class SyncFolderTests
{
    private const string LinuxHome = "/home/marshal";

    [Theory]
    [InlineData("~/Rev Sync", "/home/marshal/Rev Sync")]
    [InlineData("$HOME/Rev Sync", "/home/marshal/Rev Sync")]
    [InlineData("${HOME}/Documents/Rev Sync", "/home/marshal/Documents/Rev Sync")]
    [InlineData("/mnt/data/Rev Sync", "/mnt/data/Rev Sync")]
    [InlineData("  ~/Rev Sync  ", "/home/marshal/Rev Sync")]
    public void LinuxSettingsBecomeRealPaths(string setting, string expected) =>
        Assert.Equal(expected, SyncService.ExpandPath(setting, LinuxHome, windows: false));

    [Theory]
    [InlineData(@"%USERPROFILE%\iCloudDrive\Rev Sync")]   // the add-on's original (Windows) default
    [InlineData(@"C:\Users\marshal\Rev Sync")]
    [InlineData("Rev Sync")]                               // not a full path
    public void WindowsStyleSettingsAreIgnoredOnLinux(string setting) =>
        Assert.Null(SyncService.ExpandPath(setting, LinuxHome, windows: false));

    [Fact]
    public void OnWindowsTheUsualVariablesAreExpanded()
    {
        if (!OperatingSystem.IsWindows()) return;   // %USERPROFILE% only exists on Windows
        string expanded = SyncService.ExpandPath(@"%USERPROFILE%\iCloudDrive\Rev Sync", LinuxHome, windows: true)!;
        Assert.DoesNotContain("%", expanded);
        Assert.EndsWith(@"iCloudDrive\Rev Sync", expanded);
    }

    [Fact]
    public void AnkiIsLookedForWhereLinuxKeepsIt()
    {
        var folders = SyncService.AnkiDataFolders(LinuxHome, "", null, windows: false).ToList();
        Assert.Equal(Path.Combine(LinuxHome, ".local", "share", "Anki2"), folders[0]);
        Assert.Contains(Path.Combine(LinuxHome, ".var", "app", "net.ankiweb.Anki", "data", "Anki2"), folders);   // Flatpak
        var custom = SyncService.AnkiDataFolders(LinuxHome, "", "/data/xdg", windows: false).First();
        Assert.Equal(Path.Combine("/data/xdg", "Anki2"), custom);
    }

    [Fact]
    public void OnWindowsAnkiIsInAppData() =>
        Assert.Equal([Path.Combine(@"C:\Users\m\AppData\Roaming", "Anki2")],
                     SyncService.AnkiDataFolders(@"C:\Users\m", @"C:\Users\m\AppData\Roaming", null, windows: true));

    [Fact]
    public void TheSuggestedFolderIsInICloudOnWindowsAndInHomeOnLinux()
    {
        Assert.Equal(Path.Combine(LinuxHome, "Rev Sync"), SyncService.UsualFolderFor(LinuxHome, windows: false));
        Assert.Equal(Path.Combine(@"C:\Users\m", "iCloudDrive", "Rev Sync"), SyncService.UsualFolderFor(@"C:\Users\m", windows: true));
    }
}
