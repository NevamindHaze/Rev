using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Rev.Core.Data;

namespace Rev.Linux;

/// <summary>The app itself. Runs first when Rev starts.</summary>
public partial class App : Application
{
    /// <summary>Your decks, cards and history. Opened once when Rev starts.</summary>
    public static RevDatabase Database { get; set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // For testing only: starting Rev with REV_THEME=dark (or light) overrides the desktop's setting.
        RequestedThemeVariant = Environment.GetEnvironmentVariable("REV_THEME") switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,   // follow the desktop's light/dark setting
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                Database = RevDatabase.OpenDefault();
                desktop.MainWindow = new MainWindow();
            }
            catch (Exception error)
            {
                desktop.MainWindow = new Window
                {
                    Title = "Rev", Width = 560, Height = 220, Padding = new Thickness(28),
                    Content = new TextBlock
                    {
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Text = $"Rev couldn't open its data in {RevDatabase.DefaultFolder}.\n\n{error.Message}",
                    },
                };
            }
            desktop.Exit += (_, _) => Database?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
