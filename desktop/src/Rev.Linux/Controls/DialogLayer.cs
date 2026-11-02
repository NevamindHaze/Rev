using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Rev.Linux.Controls;

/// <summary>
/// The layer every Rev window has on top of its screen, where questions like "Delete this deck?" appear:
/// the window dims and a card shows in the middle. Clicks and keys can't reach the screen underneath
/// until the question is answered. (A layer inside the window, rather than a separate little window,
/// looks and behaves the same on every Linux desktop.)
/// </summary>
public sealed class DialogLayer : Panel
{
    private readonly Stack<(Control Content, IInputElement? FocusBefore, TaskCompletionSource Done)> open = new();

    public DialogLayer()
    {
        IsVisible = false;
        ZIndex = 1000;
        this.Colour(BackgroundProperty, "ShadeBrush");
        // Tab moves between the dialog's own buttons only.
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    /// <summary>The dialog layer of the window that <paramref name="anything"/> is in.</summary>
    public static DialogLayer? For(Visual? anything)
    {
        var top = TopLevel.GetTopLevel(anything);
        return top?.GetVisualDescendants().OfType<DialogLayer>().FirstOrDefault();
    }

    public bool IsShowing => open.Count > 0;

    /// <summary>Shows <paramref name="content"/> centred over the window until <see cref="Close"/> is called for it.</summary>
    public Task Show(Control content, bool centred = true)
    {
        var focusBefore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (centred)
        {
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
        }
        foreach (var child in Children) child.IsVisible = false;   // only the newest question is shown
        Children.Add(content);
        IsVisible = true;
        var done = new TaskCompletionSource();
        open.Push((content, focusBefore, done));
        return done.Task;
    }

    /// <summary>Closes a dialog and gives the keyboard back to whatever had it before.</summary>
    public void Close(Control content)
    {
        if (open.Count == 0 || open.Peek().Content != content) return;
        var (_, focusBefore, done) = open.Pop();
        Children.Remove(content);
        if (Children.Count > 0) Children[^1].IsVisible = true;
        IsVisible = Children.Count > 0;
        if (focusBefore is InputElement element && element.GetVisualRoot() != null) element.Focus();
        done.TrySetResult();
    }
}
