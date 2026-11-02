using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Rev.Core.Text;

namespace Rev.Linux.Controls;

/// <summary>
/// The text box for a card's text in the editor. **bold** and *italic* words show as real bold and italic
/// while you type, and their stars are hidden (they're still in the text, which is what gets saved).
/// The arrow keys, Backspace and Delete step over the hidden stars, so they never get in the way.
/// Looks like Rev's other text boxes: a soft rounded field with a teal ring while you type.
/// </summary>
public sealed class CardTextBox : Border
{
    private readonly TextEditor editor = new()
    {
        WordWrap = true,
        ShowLineNumbers = false,
        FontSize = 19,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Brushes.Transparent,
    };

    public CardTextBox()
    {
        this.Colour(BackgroundProperty, "SoftFillBrush").Colour(BorderBrushProperty, "TealBrush");
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(16, 12);
        Child = editor;

        editor.FontFamily = FontFamily.Default;
        editor.Colour(TextEditor.ForegroundProperty, "InkBrush");
        editor.TextArea.Colour(TextArea.SelectionBrushProperty, "TealBrush")
            .Colour(TextArea.SelectionForegroundProperty, "OnTealBrush");
        editor.TextArea.SelectionCornerRadius = 0;
        editor.TextArea.SelectionBorder = null;
        editor.Options.AcceptsTab = false;              // Tab moves to the next box
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.HighlightCurrentLine = false;
        editor.TextArea.TextView.ElementGenerators.Add(new HiddenMarkers());
        editor.TextArea.TextView.LineTransformers.Add(new Formatting());
        editor.TextArea.AddHandler(KeyDownEvent, TextArea_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        editor.TextChanged += (_, e) => TextChanged?.Invoke(this, e);

        // The teal ring while you type in it.
        editor.TextArea.GotFocus += (_, _) => BorderThickness = new Thickness(2);
        editor.TextArea.LostFocus += (_, _) => BorderThickness = new Thickness(0);
    }

    public event EventHandler? TextChanged;

    public string Text
    {
        get => editor.Text;
        set => editor.Text = value;
    }

    public int SelectionStart => editor.SelectionStart;
    public int SelectionLength => editor.SelectionLength;
    public bool IsTyping => editor.TextArea.IsKeyboardFocusWithin;

    public void Focus() => editor.TextArea.Focus();

    public void Select(int start, int length) => editor.Select(start, length);

    /// <summary>
    /// Replaces the text (as one step, so Ctrl+Z undoes it) and selects part of it, e.g. after Bold.
    /// </summary>
    public void ReplaceText(string text, int selectionStart, int selectionLength)
    {
        editor.Document.Replace(0, editor.Document.TextLength, text);
        editor.Select(selectionStart, selectionLength);
    }

    // ---- Stepping over the hidden stars ----

    private TextDocument Document => editor.Document;

    /// <summary>True if the character at this place is a hidden ** or * marker.</summary>
    private bool IsMarker(int offset)
    {
        var line = Document.GetLineByOffset(offset);
        int relative = offset - line.Offset;
        return CardText.Layout(Document.GetText(line)).Markers.Any(m => relative >= m.Start && relative < m.End);
    }

    /// <summary>Where → goes: just past the next character you can see (or the end of the line).</summary>
    private int NextStop(int offset)
    {
        var line = Document.GetLineByOffset(offset);
        int at = offset;
        while (at < line.EndOffset && IsMarker(at)) at++;
        if (at < line.EndOffset) return at + 1;
        if (at > offset) return at;   // only hidden stars left: stop at the end, after them (out of the bold)
        return line.NextLine?.Offset ?? offset;
    }

    /// <summary>Where ← goes: just before the previous character you can see (or the start of the line).</summary>
    private int PreviousStop(int offset)
    {
        var line = Document.GetLineByOffset(offset);
        int at = offset;
        while (at > line.Offset && IsMarker(at - 1)) at--;
        if (at > line.Offset) return at - 1;
        if (at < offset) return at;
        return line.PreviousLine?.EndOffset ?? offset;
    }

    private void TextArea_KeyDown(object? sender, KeyEventArgs e)
    {
        // Word jumps, Up/Down, Home/End and the rest work as usual.
        if ((e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None) return;
        bool shift = e.KeyModifiers == KeyModifiers.Shift;
        var area = editor.TextArea;
        int caret = area.Caret.Offset;

        switch (e.Key)
        {
            case Key.Right or Key.Left when shift:
            {
                int target = e.Key == Key.Right ? NextStop(caret) : PreviousStop(caret);
                // Keep the end where the selection started and move the other end.
                int anchor = editor.SelectionLength == 0 ? caret
                    : caret == editor.SelectionStart ? editor.SelectionStart + editor.SelectionLength : editor.SelectionStart;
                area.Caret.Offset = target;
                area.Selection = Selection.Create(area, anchor, target);
                break;
            }
            case Key.Right or Key.Left:
                if (editor.SelectionLength > 0)
                    area.Caret.Offset = e.Key == Key.Right ? editor.SelectionStart + editor.SelectionLength : editor.SelectionStart;
                else
                    area.Caret.Offset = e.Key == Key.Right ? NextStop(caret) : PreviousStop(caret);
                area.ClearSelection();
                break;
            case Key.Back when editor.SelectionLength == 0 && !shift:
            {
                var line = Document.GetLineByOffset(caret);
                int at = caret;
                while (at > line.Offset && IsMarker(at - 1)) at--;
                if (at == line.Offset)
                {
                    area.Caret.Offset = at;   // joining two lines: let the editor do it, from before the stars
                    return;
                }
                Document.Remove(at - 1, 1);
                area.Caret.Offset = RemoveEmptyMarkers(at - 1);
                break;
            }
            case Key.Delete when editor.SelectionLength == 0 && !shift:
            {
                var line = Document.GetLineByOffset(caret);
                int at = caret;
                while (at < line.EndOffset && IsMarker(at)) at++;
                if (at == line.EndOffset)
                {
                    area.Caret.Offset = at;
                    return;
                }
                Document.Remove(at, 1);
                area.Caret.Offset = Math.Min(caret, RemoveEmptyMarkers(at));
                break;
            }
            default:
                return;
        }
        area.Caret.BringCaretToView();
        e.Handled = true;
    }

    /// <summary>
    /// Deleting the last letter of a bold or italic word leaves "****" or "**" behind, which would show as
    /// stars. Takes those out. Returns where the caret should be.
    /// </summary>
    private int RemoveEmptyMarkers(int at)
    {
        string Around(int from, int length) =>
            from >= 0 && from + length <= Document.TextLength ? Document.GetText(from, length) : "";

        if (Around(at - 2, 4) == "****")
        {
            Document.Remove(at - 2, 4);
            return at - 2;
        }
        if (Around(at - 1, 2) == "**" && Around(at - 2, 1) != "*" && Around(at + 1, 1) != "*")
        {
            Document.Remove(at - 1, 2);
            return at - 1;
        }
        return at;
    }

    // ---- Drawing ----

    /// <summary>Draws each ** or * marker as nothing.</summary>
    private sealed class HiddenMarkers : VisualLineElementGenerator
    {
        private MarkerRange? MarkerFrom(int offset, out int lineOffset)
        {
            var line = CurrentContext.Document.GetLineByOffset(offset);
            lineOffset = line.Offset;
            int relative = offset - line.Offset;
            return CardText.Layout(CurrentContext.Document.GetText(line)).Markers
                .Where(m => m.Start >= relative).OrderBy(m => m.Start).Cast<MarkerRange?>().FirstOrDefault();
        }

        public override int GetFirstInterestedOffset(int startOffset) =>
            MarkerFrom(startOffset, out int lineOffset) is { } marker ? lineOffset + marker.Start : -1;

        public override VisualLineElement? ConstructElement(int offset) =>
            MarkerFrom(offset, out int lineOffset) is { } marker && lineOffset + marker.Start == offset
                ? new HiddenElement(marker.Length) : null;
    }

    private sealed class HiddenElement(int length) : VisualLineElement(1, length)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) =>
            new EmptyRun(TextRunProperties);

        // No caret stop of its own: the text on either side has those.
        public override int GetNextCaretPosition(int visualColumn, AvaloniaEdit.Document.LogicalDirection direction, CaretPositioningMode mode) => -1;
    }

    private sealed class EmptyRun(TextRunProperties properties) : DrawableTextRun
    {
        public override TextRunProperties Properties { get; } = properties;
        public override Size Size => default;
        public override double Baseline => 0;
        public override void Draw(DrawingContext drawingContext, Point origin) { }
    }

    /// <summary>Makes the bold and italic words bold and italic.</summary>
    private sealed class Formatting : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            foreach (var range in CardText.Layout(CurrentContext.Document.GetText(line)).Styled)
            {
                if (range.Length == 0) continue;
                ChangeLinePart(line.Offset + range.Start, line.Offset + range.Start + range.Length, element =>
                {
                    var face = element.TextRunProperties.Typeface;
                    element.TextRunProperties.SetTypeface(new Typeface(face.FontFamily,
                        range.Italic ? FontStyle.Italic : face.Style,
                        range.Bold ? FontWeight.ExtraBold : face.Weight));
                });
            }
        }
    }
}
