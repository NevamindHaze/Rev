using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Rev.Linux.Controls;

/// <summary>Small helpers for building screens from code: icons, colours by name, and button contents.</summary>
public static class Ui
{
    /// <summary>An icon (a shape, so it looks the same everywhere) in the colour of the text around it.</summary>
    public static MaterialIcon Icon(MaterialIconKind kind, double size = 16) =>
        new() { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>An icon next to a label, e.g. the contents of a "+ New deck" button.</summary>
    public static StackPanel IconAndText(MaterialIconKind kind, string text, double iconSize = 15, double gap = 10) => new()
    {
        Orientation = Orientation.Horizontal,
        Children =
        {
            new MaterialIcon { Kind = kind, Width = iconSize, Height = iconSize, Margin = new Thickness(0, 0, gap, 0), VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    /// <summary>Uses one of Rev's colours by name, so it follows light/dark mode (like DynamicResource in XAML).</summary>
    public static T Colour<T>(this T control, AvaloniaProperty property, string key) where T : AvaloniaObject
    {
        control.Bind(property, new DynamicResourceExtension(key));
        return control;
    }

    /// <summary>What screen readers (and the automatic UI tests) call a control.</summary>
    public static T Named<T>(this T control, string name) where T : Control
    {
        AutomationProperties.SetName(control, name);
        return control;
    }

    private static readonly (string Background, string Foreground)[] DeckPalette =
    [
        ("CoralBrush", "OnCoralBrush"),
        ("TealBrush", "OnTealBrush"),
        ("MustardBrush", "OnMustardBrush"),
        ("SageBrush", "OnSageBrush"),
    ];

    /// <summary>A deck's colours (and the colour of text on them), taking turns down the list of decks.</summary>
    public static (string Background, string Foreground) DeckColours(int index) => DeckPalette[index % DeckPalette.Length];

    /// <summary>A button in one of Rev's styles, e.g. Ui.Button("rev teal", "Save").</summary>
    public static Button Button(string classes, object content)
    {
        var button = new Button { Content = content };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) button.Classes.Add(name);
        return button;
    }
}

public static class LayoutExtensions
{
    /// <summary>Centres a control in the space it's given.</summary>
    public static T Center<T>(this T control) where T : Layoutable
    {
        control.HorizontalAlignment = HorizontalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }
}
