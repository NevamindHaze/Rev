using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Rev.Linux;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithRevFont().LogToTrace();

    /// <summary>Rev's font (Nunito, bundled) as the font for everything, menus and tooltips too.</summary>
    public static AppBuilder WithRevFont(this AppBuilder builder) =>
        builder
            .ConfigureFonts(fonts => fonts.AddFontCollection(
                new EmbeddedFontCollection(new Uri("fonts:Rev", UriKind.Absolute), new Uri("avares://Rev/Assets/Fonts", UriKind.Absolute))))
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Rev#Nunito" });
}
