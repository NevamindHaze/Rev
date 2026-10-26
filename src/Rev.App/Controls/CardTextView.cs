using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Rev.Core.Media;
using Rev.Core.Text;
using WpfImage = System.Windows.Controls.Image;

namespace Rev.App.Controls;

/// <summary>
/// Shows a card's text nicely: **bold** and *italic* as real formatting, list items with wrapped
/// lines lined up under the text (not under the bullet), blank lines, and images.
/// Use it from code: new CardTextView { Text = card.Front, TextSize = 26 }.
/// </summary>
public sealed class CardTextView : StackPanel
{
    private string text = "";
    private double textSize = 22;

    public string Text { get => text; set { text = value; Build(); } }
    public double TextSize { get => textSize; set { textSize = value; Build(); } }

    private void Build()
    {
        Children.Clear();
        foreach (var line in CardText.Lines(text))
        {
            switch (line)
            {
                case CardLine.Text plain:
                    Children.Add(Paragraph(plain.Value));
                    break;

                case CardLine.ListItem item:
                    // Two columns: the bullet or number, then the text. Wrapped lines stay in the text column.
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(textSize * 1.4) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(new TextBlock { Text = item.Marker, FontSize = textSize });
                    var body = Paragraph(item.Value);
                    Grid.SetColumn(body, 1);
                    row.Children.Add(body);
                    Children.Add(row);
                    break;

                case CardLine.Image image:
                    Children.Add(ImageFor(image.Name));
                    break;

                case CardLine.Blank:
                    Children.Add(new Border { Height = textSize * 0.4 });
                    break;
            }
        }
    }

    private TextBlock Paragraph(string value)
    {
        var block = new TextBlock { FontSize = textSize, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        RichText.Fill(block, value);
        return block;
    }

    private FrameworkElement ImageFor(string name)
    {
        if (MediaStore.PathFor(name) is { } path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;   // read now, so the file isn't kept locked
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                return new WpfImage { Source = bitmap, MaxHeight = 380, HorizontalAlignment = HorizontalAlignment.Left,
                                      Margin = new Thickness(0, 6, 0, 6) };
            }
            catch (Exception) { /* a damaged image file: fall through to the message */ }
        }
        var missing = new TextBlock { Text = $"Image not found: {name}", FontSize = 14, Margin = new Thickness(0, 4, 0, 4) };
        missing.SetResourceReference(TextBlock.ForegroundProperty, "MutedInkBrush");
        return missing;
    }
}

/// <summary>Fills a TextBlock with text whose **bold** and *italic* markers become real formatting.</summary>
public static class RichText
{
    public static void Fill(TextBlock block, string text)
    {
        block.Inlines.Clear();
        foreach (var span in CardText.Spans(text))
        {
            block.Inlines.Add(new Run(span.Value)
            {
                FontWeight = span.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = span.Italic ? FontStyles.Italic : FontStyles.Normal,
            });
        }
    }
}
