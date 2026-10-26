using System.Windows;
using Rev.App.Theme;
using Rev.Core.Data;

namespace Rev.App;

/// <summary>The app itself. Runs first when Rev starts.</summary>
public partial class App : Application
{
    /// <summary>Your decks, cards and history. Opened once when Rev starts.</summary>
    public static RevDatabase Database { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Load light or dark colours (matching Windows) before the first window appears.
        ThemeManager.Start(this);

        try
        {
            Database = RevDatabase.OpenDefault();
        }
        catch (Exception error)
        {
            MessageBox.Show($"Rev couldn't open its data in {RevDatabase.DefaultFolder}.\n\n{error.Message}",
                            "Rev", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Database?.Dispose();
        base.OnExit(e);
    }
}
