using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Rev.App.Theme;

/// <summary>
/// Follows Windows' light/dark setting (Settings → Personalisation → Colours → "Choose your app mode").
///
/// In plain words: when the app starts, and whenever you change that setting, we read it,
/// load the matching colour file (Colors.Light.xaml or Colors.Dark.xaml), and colour every
/// window's title bar to match Rev's background.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    private static ResourceDictionary? currentColors;

    /// <summary>Call once when the app starts.</summary>
    public static void Start(Application app)
    {
        Apply(app, ReadWindowsIsDark());

        // Windows tells running apps when personal settings change. We check whether dark mode changed.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            app.Dispatcher.Invoke(() =>
            {
                bool dark = ReadWindowsIsDark();
                if (dark != IsDark) Apply(app, dark);
            });
        };

        // Every window that opens gets a title bar in Rev's colours.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ColorTitleBar((Window)sender)));
    }

    /// <summary>Windows stores "apps use light theme" as 1 (light) or 0 (dark) in the registry.</summary>
    private static bool ReadWindowsIsDark()
    {
        // For testing only: starting Rev with REV_THEME=dark (or light) overrides Windows' setting.
        var forced = Environment.GetEnvironmentVariable("REV_THEME");
        if (forced is "dark" or "light") return forced == "dark";

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static void Apply(Application app, bool dark)
    {
        IsDark = dark;
        var colors = new ResourceDictionary
        {
            Source = new Uri(dark ? "Theme/Colors.Dark.xaml" : "Theme/Colors.Light.xaml", UriKind.Relative)
        };

        // Swap the old colour file for the new one. Everything using DynamicResource updates by itself.
        var dictionaries = app.Resources.MergedDictionaries;
        if (currentColors != null) dictionaries.Remove(currentColors);
        dictionaries.Insert(0, colors);
        currentColors = colors;

        foreach (Window window in app.Windows) ColorTitleBar(window);
    }

    // ---- Title bar colours (Windows 11) ----

    // These numbers come from Windows' own documentation for DwmSetWindowAttribute.
    private const int UseDarkMode = 20;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static void ColorTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;   // the window isn't on screen yet

        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, UseDarkMode, ref dark, sizeof(int));

        if (Application.Current.TryFindResource("TitleBarColor") is Color bar
            && Application.Current.TryFindResource("TitleTextColor") is Color text)
        {
            int barValue = ToColorRef(bar);
            int textValue = ToColorRef(text);
            DwmSetWindowAttribute(handle, CaptionColor, ref barValue, sizeof(int));
            DwmSetWindowAttribute(handle, TextColor, ref textValue, sizeof(int));
        }
    }

    /// <summary>Windows wants colours as 0x00BBGGRR (blue, green, red), the reverse of the usual order.</summary>
    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
