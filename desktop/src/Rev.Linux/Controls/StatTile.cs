using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Rev.Linux.Controls;

/// <summary>
/// A small number tile, like "12 · Due today": a coloured circle with an icon, a big number and a label.
/// In XAML: &lt;controls:StatTile Value="12" Label="Due today" Icon="ClockOutline" Tint="{DynamicResource TealBrush}" /&gt;
/// </summary>
public sealed class StatTile : Border
{
    // "Styled properties" are Avalonia's way of making a setting that XAML can fill in.
    public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<StatTile, string>(nameof(Value), "0");
    public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<StatTile, string>(nameof(Label), "");
    public static readonly StyledProperty<MaterialIconKind> IconProperty = AvaloniaProperty.Register<StatTile, MaterialIconKind>(nameof(Icon));
    public static readonly StyledProperty<IBrush?> TintProperty = AvaloniaProperty.Register<StatTile, IBrush?>(nameof(Tint));

    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public MaterialIconKind Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public IBrush? Tint { get => GetValue(TintProperty); set => SetValue(TintProperty, value); }

    private readonly Border circle = new() { Width = 40, Height = 40, CornerRadius = new CornerRadius(20) };
    private readonly MaterialIcon icon = new() { Width = 17, Height = 17 };
    private readonly TextBlock number = new() { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 30, FontWeight = FontWeight.Bold };
    private readonly TextBlock label = new() { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 14, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };

    public StatTile()
    {
        CornerRadius = new CornerRadius(24);
        Padding = new Thickness(8, 18);
        this.Colour(BackgroundProperty, "SoftFillBrush");
        icon.Colour(MaterialIcon.ForegroundProperty, "DarkTextBrush");
        number.Colour(TextBlock.ForegroundProperty, "InkBrush");
        label.Colour(TextBlock.ForegroundProperty, "MutedInkBrush");
        circle.Child = icon;
        Child = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { circle, number, label } };
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == LabelProperty || change.Property == IconProperty || change.Property == TintProperty)
            Update();
    }

    private void Update()
    {
        number.Text = Value;
        label.Text = Label;
        icon.Kind = Icon;
        circle.Background = Tint;
    }
}
