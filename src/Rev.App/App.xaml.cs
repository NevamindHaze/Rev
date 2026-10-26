using System.Windows;
using Rev.App.Theme;

namespace Rev.App;

/// <summary>The app itself. Runs first when Rev starts.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Load light or dark colours (matching Windows) before the first window appears.
        ThemeManager.Start(this);
        base.OnStartup(e);
    }
}
