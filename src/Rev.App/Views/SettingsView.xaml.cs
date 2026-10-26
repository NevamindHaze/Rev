using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Rev.App.Controls;
using Rev.Core.Anki;
using Rev.Core.Backup;
using Rev.Core.Media;
using Rev.Core.Data;
using Rev.Core.Scheduling;
using Rev.Core.Text;

namespace Rev.App.Views;

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
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MainWindow.Current.GoBack()), Key.Escape, ModifierKeys.None));

        NameBox.Text = settings.UserName;
        PerDayBox.Text = settings.NewCardsPerDay.ToString();
        (settings.Scheduler == SchedulerKind.Sm2 ? Sm2Choice : FsrsChoice).IsChecked = true;
        RetentionSlider.Value = Math.Round(settings.DesiredRetention * 100);
        UpdateRetention();

        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = $"Rev for Windows {version?.ToString(3)}";
        FolderText.Text = $"Your decks and progress are kept in {RevDatabase.DefaultFolder}";
        loading = false;
    }

    public void Refresh() { }

    /// <summary>Saves the settings with one change made.</summary>
    private void Save(Func<AppSettings, AppSettings> change)
    {
        if (loading) return;
        settings = change(settings);
        App.Database.SaveSettings(settings);
    }

    // ---- You ----

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Save(s => s with { UserName = NameBox.Text.Trim() });

    // ---- New cards per day ----

    private void Fewer_Click(object sender, RoutedEventArgs e) => SetPerDay(settings.NewCardsPerDay - 5);

    private void More_Click(object sender, RoutedEventArgs e) => SetPerDay(settings.NewCardsPerDay + 5);

    private void SetPerDay(int value)
    {
        value = Math.Clamp(value, 0, MaxNewCardsPerDay);
        PerDayBox.Text = value.ToString();   // TextChanged saves it
    }

    /// <summary>Only digits can be typed in the number box.</summary>
    private void PerDayBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void PerDayBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!int.TryParse(PerDayBox.Text, out int value)) return;   // e.g. empty while you retype it
        value = Math.Clamp(value, 0, MaxNewCardsPerDay);
        if (value.ToString() != PerDayBox.Text) PerDayBox.Text = value.ToString();
        Save(s => s with { NewCardsPerDay = value });
    }

    // ---- Scheduling ----

    private void Scheduler_Checked(object sender, RoutedEventArgs e)
    {
        var kind = sender == Sm2Choice ? SchedulerKind.Sm2 : SchedulerKind.Fsrs;
        Save(s => s with { Scheduler = kind });
        if (RetentionPanel != null) UpdateRetention();
    }

    private void RetentionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RetentionText is null) return;   // still being built
        Save(s => s with { DesiredRetention = Math.Round(RetentionSlider.Value) / 100 });
        UpdateRetention();
    }

    /// <summary>The target memory only applies to FSRS, so it's hidden for SM-2.</summary>
    private void UpdateRetention()
    {
        RetentionText.Text = $"Target memory: {Math.Round(RetentionSlider.Value)}%";
        RetentionPanel.Visibility = FsrsChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Anki ----

    private async void ImportAnki_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Anki deck",
            Filter = "Anki decks (*.apkg; *.colpkg)|*.apkg;*.colpkg",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(OwnerWindow) != true) return;

        // Reading a big file can take a few seconds, so it happens in the background
        // while Rev shows a busy cursor. (The file is only read, never changed.)
        string path = dialog.FileName;
        string media = MediaStore.Folder;
        IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var contents = await Task.Run(() => ApkgReader.Read(path, media));
            var result = AnkiImporter.Import(App.Database, contents);
            Mouse.OverrideCursor = null;
            RevDialog.Tell(OwnerWindow, "Anki deck imported", result.Summary);
        }
        catch (Exception error)
        {
            Mouse.OverrideCursor = null;
            RevDialog.Tell(OwnerWindow, "That file couldn't be imported", error.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            IsEnabled = true;
            ImportButton.Focus();
        }
    }

    // ---- Backup ----

    private Window? OwnerWindow => Window.GetWindow(this);

    /// <summary>Where restore keeps a copy of your data from just before, in case you need it back.</summary>
    private static string SafetyFolder => Path.Combine(RevDatabase.DefaultFolder, "Backups");

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save backup",
            FileName = BackupService.SuggestedFileName(DateTime.Now),
            DefaultExt = ".json",
            Filter = "Rev backup (*.json)|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(OwnerWindow) != true) return;
        try
        {
            var backup = BackupService.Make(App.Database);
            BackupService.Save(backup, dialog.FileName);
            RevDialog.Tell(OwnerWindow, "Backup saved",
                $"{Summary(backup)} saved to {dialog.FileName}.");
        }
        catch (Exception error)
        {
            RevDialog.Tell(OwnerWindow, "The backup wasn't saved", error.Message);
        }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore from backup",
            Filter = "Rev backup (*.json)|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(OwnerWindow) != true) return;

        BackupFile backup;
        try
        {
            backup = BackupService.Load(dialog.FileName);
        }
        catch (Exception error)
        {
            RevDialog.Tell(OwnerWindow, "That file can't be restored", error.Message);
            return;
        }

        var made = BackupService.FromSeconds(backup.ExportedAt);
        bool sure = RevDialog.Confirm(OwnerWindow, "Replace everything with this backup?",
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
            RevDialog.Tell(OwnerWindow, "Nothing was changed", $"The backup couldn't be restored: {error.Message}");
            return;
        }

        // Show the restored settings, then confirm.
        loading = true;
        settings = App.Database.GetSettings();
        NameBox.Text = settings.UserName;
        PerDayBox.Text = settings.NewCardsPerDay.ToString();
        (settings.Scheduler == SchedulerKind.Sm2 ? Sm2Choice : FsrsChoice).IsChecked = true;
        RetentionSlider.Value = Math.Round(settings.DesiredRetention * 100);
        UpdateRetention();
        loading = false;
        RevDialog.Tell(OwnerWindow, "Backup restored", $"Rev now has {Summary(backup)}.");
    }

    /// <summary>"3 decks, 120 cards".</summary>
    private static string Summary(BackupFile backup) =>
        $"{backup.Decks.Count.Counted("deck")}, {backup.Decks.Sum(d => d.Cards.Count).Counted("card")}";

    // ---- About ----

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{RevDatabase.DefaultFolder}\"") { UseShellExecute = true });

    private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.GoBack();
}
