using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rev.App.Controls;

/// <summary>
/// A small number tile. Use it like:
///   &lt;controls:StatTile Value="12" Label="Due today" Glyph="&#xE823;" Tint="{DynamicResource TealBrush}" /&gt;
/// "Glyph" is a symbol from Windows' icon font (Segoe Fluent Icons).
/// </summary>
public partial class StatTile : UserControl
{
    public StatTile() => InitializeComponent();

    // "Dependency properties" are WPF's way of making a setting that XAML can fill in and that
    // updates the screen when it changes. Each one below is one setting of the tile.

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatTile), new PropertyMetadata("0"));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    public static readonly DependencyProperty TintProperty =
        DependencyProperty.Register(nameof(Tint), typeof(Brush), typeof(StatTile), new PropertyMetadata(Brushes.Gray));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush Tint { get => (Brush)GetValue(TintProperty); set => SetValue(TintProperty, value); }
}
