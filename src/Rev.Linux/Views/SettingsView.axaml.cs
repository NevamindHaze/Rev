using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rev.Core.Anki;
using Rev.Core.Backup;
using Rev.Core.Data;
using Rev.Core.Media;
using Rev.Core.Scheduling;
using Rev.Core.Text;
using Rev.Linux.Controls;

namespace Rev.Linux.Views;

/// <summary>Settings. Each change is saved to the database at once (there's no Save button to forget).</summary>
public partial class SettingsView : UserControl, IScreen
{
    private const int MaxNewCardsPerDay = 500;
    private AppSettings settings;
    private bool loading = true;   // true while filling in the boxes, so that doesn't count as a change

    public SettingsView()
    {
        InitializeComponent();
        settings = App.Database.GetSettings();
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Escape), Command = new RelayCommand(() => MainWindow.Current.GoBack()) });

        ShowSettings();
        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = $"Rev for {(OperatingSystem.IsLinux() ? "Linux" : "desktop")} {version?.ToString(3)}";
        FolderText.Text = $"Your decks and progress are kept in {RevDatabase.DefaultFolder}";
        UpdateSyncStatus();

        NameBox.TextChanged += (_, _) => Save(s => s with { UserName = (NameBox.Text ?? "").Trim() });
        PerDayBox.TextChanged += (_, _) => PerDayChanged();
        // Only digits can be typed in the number box.
        PerDayBox.AddHandler(TextInputEvent, (_, e) => e.Handled = e.Text is { } t && !t.All(char.IsDigit), RoutingStrategies.Tunnel);
        RetentionSlider.ValueChanged += (_, _) =>
        {
            Save(s => s with { DesiredRetention = Math.Round(RetentionSlider.Value) / 100 });
            UpdateRetention();
        };
        loading = false;
    }

    public void Refresh() => UpdateSyncStatus();

    private void ShowSettings()
    {
        NameBox.Text = settings.UserName;
        PerDayBox.Text = settings.NewCardsPerDay.ToString();
        (settings.Scheduler == SchedulerKind.Sm2 ? Sm2Choice : FsrsChoice).IsChecked = true;
        RetentionSlider.Value = Math.Round(settings.DesiredRetention * 100);
        UpdateRetention();
    }

    // ---- Sync with Anki ----

    private void UpdateSyncStatus()
    {
        string? folder = SyncRunner.Folder;
        SyncStatus.Text = folder is null ? "Not set up yet. Choose the Rev Sync folder to start."
            : SyncRunner.IsOn ? $"Syncing through {folder}"
            : $"Folder chosen ({folder}), but not syncing yet. Click Sync now to start.";
        SyncLast.Text = SyncRunner.LastTime is { } time
            ? $"Last synced {time:d MMMM, HH:mm}: {SyncRunner.LastMessage}"
            : SyncRunner.LastMessage ?? "";
        SyncLast.IsVisible = SyncLast.Text.Length > 0;
        SyncNowButton.IsEnabled = folder is not null;
        SyncOffButton.IsVisible = folder is not null;
    }

    private async void ChooseSyncFolder_Click(object? sender, RoutedEventArgs e)
    {
        string suggested = SyncRunner.SuggestedFolder;
        // The add-on makes ~/Rev Sync itself, but if Anki hasn't run yet, make it so it can be chosen.
        if (!Directory.Exists(suggested) && suggested == Core.Sync.SyncService.UsualFolder) Directory.CreateDirectory(suggested);
        var folder = await FileDialogs.ChooseFolder(this, "Choose the Rev Sync folder (the one the Rev Sync add-on in Anki uses)", suggested);
        if (folder is null) return;
        await SyncRunner.SetUp(this, folder);
        UpdateSyncStatus();
    }

    private async void SyncNow_Click(object? sender, RoutedEventArgs e)
    {
        if (SyncRunner.Folder is not { } folder) return;
        if (!SyncRunner.IsOn)
        {
            await SyncRunner.SetUp(this, folder);   // the first sync: backup, preview, then sync
        }
        else
        {
            bool ok;
            string message;
            using (FileDialogs.Busy(this)) ok = SyncRunner.Run(out message);
            await RevDialog.Tell(this, ok ? "Synced with Anki" : "Sync didn't finish", message);
        }
        UpdateSyncStatus();
    }

    private async void SyncOff_Click(object? sender, RoutedEventArgs e)
    {
        if (!await RevDialog.Confirm(this, "Turn off sync with Anki?",
                "Rev will stop syncing. Your cards stay as they are, here and in Anki. " +
                "(To take this computer out of the sync completely, also delete windows.json from the Rev Sync folder.)",
                "Turn off", destructive: true)) return;
        SyncRunner.TurnOff();
        UpdateSyncStatus();
    }

    /// <summary>Saves the settings with one change made.</summary>
    private void Save(Func<AppSettings, AppSettings> change)
    {
        if (loading) return;
        settings = change(settings);
        App.Database.SaveSettings(settings);
    }

    // ---- New cards per day ----

    private void Fewer_Click(object? sender, RoutedEventArgs e) => SetPerDay(settings.NewCardsPerDay - 5);

    private void More_Click(object? sender, RoutedEventArgs e) => SetPerDay(settings.NewCardsPerDay + 5);

    private void SetPerDay(int value)
    {
        value = Math.Clamp(value, 0, MaxNewCardsPerDay);
        PerDayBox.Text = value.ToString();   // TextChanged saves it
    }

    private void PerDayChanged()
    {
        if (!int.TryParse(PerDayBox.Text, out int value)) return;   // e.g. empty while you retype it
        value = Math.Clamp(value, 0, MaxNewCardsPerDay);
        if (value.ToString() != PerDayBox.Text) PerDayBox.Text = value.ToString();
        Save(s => s with { NewCardsPerDay = value });
    }

    // ---- Scheduling ----

    private void Scheduler_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true }) return;
        var kind = sender == Sm2Choice ? SchedulerKind.Sm2 : SchedulerKind.Fsrs;
        Save(s => s with { Scheduler = kind });
        if (RetentionPanel != null) UpdateRetention();
    }

    /// <summary>The target memory only applies to FSRS, so it's hidden for SM-2.</summary>
    private void UpdateRetention()
    {
        RetentionText.Text = $"Target memory: {Math.Round(RetentionSlider.Value)}%";
        RetentionPanel.IsVisible = settings.Scheduler == SchedulerKind.Fsrs;
    }

    // ---- Anki ----

    private async void ImportAnki_Click(object? sender, RoutedEventArgs e)
    {
        var path = await FileDialogs.OpenFile(this, "Import Anki deck", "Anki decks", "*.apkg", "*.colpkg");
        if (path is null) return;

        // Reading a big file can take a few seconds, so it happens in the background
        // while Rev shows a busy cursor. (The file is only read, never changed.)
        string media = MediaStore.Folder;
        IsEnabled = false;
        try
        {
            ImportResult result;
            using (FileDialogs.Busy(this))
            {
                var contents = await Task.Run(() => ApkgReader.Read(path, media));
                result = AnkiImporter.Import(App.Database, contents);
            }
            IsEnabled = true;
            await RevDialog.Tell(this, "Anki deck imported", result.Summary);
        }
        catch (Exception error)
        {
            IsEnabled = true;
            await RevDialog.Tell(this, "That file couldn't be imported", error.Message);
        }
        finally
        {
            IsEnabled = true;
            ImportButton.Focus();
        }
    }

    private async void ExportAnki_Click(object? sender, RoutedEventArgs e) =>
        await AnkiExport.Run(this, ApkgWriter.AllDecks(App.Database), $"Rev decks {DateTime.Now:yyyy-MM-dd}");

    // ---- Backup ----

    /// <summary>Where restore keeps a copy of your data from just before, in case you need it back.</summary>
    private static string SafetyFolder => Path.Combine(RevDatabase.DefaultFolder, "Backups");

    private async void ExportBackup_Click(object? sender, RoutedEventArgs e)
    {
        var path = await FileDialogs.SaveFile(this, "Save backup", BackupService.SuggestedFileName(DateTime.Now), "Rev backup", "json");
        if (path is null) return;
        try
        {
            var backup = BackupService.Make(App.Database);
            BackupService.Save(backup, path);
            await RevDialog.Tell(this, "Backup saved", $"{Summary(backup)} saved to {path}.");
        }
        catch (Exception error)
        {
            await RevDialog.Tell(this, "The backup wasn't saved", error.Message);
        }
    }

    private async void RestoreBackup_Click(object? sender, RoutedEventArgs e)
    {
        var path = await FileDialogs.OpenFile(this, "Restore from backup", "Rev backup", "*.json");
        if (path is null) return;

        BackupFile backup;
        try
        {
            backup = BackupService.Load(path);
        }
        catch (Exception error)
        {
            await RevDialog.Tell(this, "That file can't be restored", error.Message);
            return;
        }

        var made = BackupService.FromSeconds(backup.ExportedAt);
        bool sure = await RevDialog.Confirm(this, "Replace everything with this backup?",
            $"Backup from {made:d MMMM yyyy, HH:mm}: {Summary(backup)}. Everything currently in Rev will be replaced. " +
            "(A copy of your current data is saved first, in the Backups folder.)",
            "Replace everything", destructive: true);
        if (!sure) return;

        try
        {
            BackupService.Restore(App.Database, backup, SafetyFolder);
        }
        catch (Exception error)
        {
            await RevDialog.Tell(this, "Nothing was changed", $"The backup couldn't be restored: {error.Message}");
            return;
        }

        // Show the restored settings, then confirm.
        loading = true;
        settings = App.Database.GetSettings();
        ShowSettings();
        loading = false;
        await RevDialog.Tell(this, "Backup restored", $"Rev now has {Summary(backup)}.");
    }

    /// <summary>"3 decks, 120 cards".</summary>
    private static string Summary(BackupFile backup) =>
        $"{backup.Decks.Count.Counted("deck")}, {backup.Decks.Sum(d => d.Cards.Count).Counted("card")}";

    // ---- About ----

    /// <summary>Opens the data folder in the file manager (Nemo on Linux Mint).</summary>
    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        string folder = RevDatabase.DefaultFolder;
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { folder }, UseShellExecute = false });
        }
        catch (Exception) { /* no file manager: nothing to do */ }
    }

    private void Back_Click(object? sender, RoutedEventArgs e) => MainWindow.Current.GoBack();
}
