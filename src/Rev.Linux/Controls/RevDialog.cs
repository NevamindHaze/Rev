using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Rev.Linux.Controls;

/// <summary>
/// A clear, centred question in Rev's style, shown on the window's <see cref="DialogLayer"/>.
///
/// - <see cref="Confirm"/> asks yes/no, e.g. "Delete “Pharmacology”?". For deleting (destructive = true)
///   the button is red and the keyboard starts on Cancel, so pressing Enter can never delete by accident.
/// - <see cref="Prompt"/> asks for a line of text, e.g. a deck name. Enter confirms, Esc cancels.
/// - <see cref="Choose"/> offers several buttons; <see cref="Tell"/> just says something.
/// Each one waits (await) until you've answered.
/// </summary>
public sealed class RevDialog : Border
{
    private readonly TextBox? input;
    private readonly StackPanel buttons = new()
    {
        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0),
    };
    private int chosen = -1;
    private int cancelChoice;
    private Button? defaultButton;
    private Button? firstFocus;
    private DialogLayer? layer;

    private RevDialog(string title, string? message, string? hint, string? initialText)
    {
        Width = 500;
        Padding = new Thickness(30, 28);
        CornerRadius = new CornerRadius(30);
        BoxShadow = BoxShadows.Parse("0 4 28 0 #40000000");
        this.Colour(BackgroundProperty, "SurfaceBrush");

        var body = new StackPanel();
        var titleText = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontSize = 22 };
        titleText.Classes.Add("heading");
        body.Children.Add(titleText);
        if (!string.IsNullOrEmpty(message))
        {
            var messageText = new TextBlock { Text = message, FontSize = 16, Margin = new Thickness(0, 8, 0, 0) };
            messageText.Classes.Add("muted");
            body.Children.Add(messageText);
        }
        if (hint != null)
        {
            input = new TextBox { Watermark = hint, Text = initialText ?? "", Margin = new Thickness(0, 18, 0, 0) };
            input.Classes.Add("rev");
            body.Children.Add(input);
        }
        body.Children.Add(buttons);
        Child = body;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Answer(cancelChoice);
            else if (e.Key == Key.Enter && defaultButton is { IsEnabled: true }) defaultButton.Command?.Execute(null);
            else return;
            e.Handled = true;
        };
    }

    private Button AddButton(string text, string classes, int number)
    {
        var button = Ui.Button("rev " + classes, text);
        button.MinHeight = 50;
        button.Margin = new Thickness(buttons.Children.Count == 0 ? 0 : 12, 0, 0, 0);
        button.Command = new RelayCommand(() => Answer(number));
        buttons.Children.Add(button);
        return button;
    }

    private void Answer(int number)
    {
        chosen = number;
        layer?.Close(this);
    }

    /// <summary>Shows the dialog and waits for an answer. Returns the number of the chosen button.</summary>
    private async Task<int> Run(Visual owner)
    {
        layer = DialogLayer.For(owner);
        if (layer is null) return cancelChoice;
        var shown = layer.Show(this);
        // Once it's on screen, put the keyboard on the right control.
        Dispatcher.UIThread.Post(() =>
        {
            if (input != null)
            {
                input.Focus();
                input.SelectAll();
            }
            else firstFocus?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Input);
        await shown;
        return chosen < 0 ? cancelChoice : chosen;
    }

    /// <summary>Asks a yes/no question. Returns true if you clicked the confirm button.</summary>
    public static async Task<bool> Confirm(Visual owner, string title, string message, string confirmText, bool destructive = false)
    {
        var dialog = new RevDialog(title, message, null, null);
        var cancel = dialog.AddButton("Cancel", "secondary", 0);
        var confirm = dialog.AddButton(confirmText, destructive ? "danger" : "", 1);
        if (destructive) dialog.firstFocus = cancel;
        else
        {
            dialog.defaultButton = confirm;
            dialog.firstFocus = confirm;
        }
        return await dialog.Run(owner) == 1;
    }

    /// <summary>
    /// Offers several choices, e.g. the leech question. The first choice is the safe one (Esc picks it);
    /// the last is shown as the main button. Returns the number of the chosen button (0 = first).
    /// </summary>
    public static Task<int> Choose(Visual owner, string title, string message, params string[] choices)
    {
        var dialog = new RevDialog(title, message, null, null);
        // More than two buttons need more room, so none of them gets cut off.
        if (choices.Length > 2) dialog.Width = 720;
        for (int i = 0; i < choices.Length; i++)
        {
            var button = dialog.AddButton(choices[i], i == choices.Length - 1 ? "teal" : "secondary", i);
            if (i == 0) dialog.firstFocus = button;
        }
        return dialog.Run(owner);
    }

    /// <summary>Just tells you something, with an OK button.</summary>
    public static Task Tell(Visual owner, string title, string message) => Choose(owner, title, message, "OK");

    /// <summary>Asks for a line of text. Returns it (trimmed), or null if you cancelled.</summary>
    public static async Task<string?> Prompt(Visual owner, string title, string? message, string initialText,
                                             string confirmText, string hint)
    {
        var dialog = new RevDialog(title, message, hint, initialText);
        dialog.AddButton("Cancel", "secondary", 0);
        var confirm = dialog.AddButton(confirmText, "", 1);
        dialog.defaultButton = confirm;
        // An empty name can't be saved, so the button stays greyed out until you type something.
        void Update() => confirm.IsEnabled = dialog.input!.Text?.Trim().Length > 0;
        dialog.input!.TextChanged += (_, _) => Update();
        Update();
        return await dialog.Run(owner) == 1 ? dialog.input.Text?.Trim() ?? "" : null;
    }
}
