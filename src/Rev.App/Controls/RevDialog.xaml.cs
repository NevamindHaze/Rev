using System.Windows;
using System.Windows.Controls;

namespace Rev.App.Controls;

/// <summary>
/// A clear, centred question box in Rev's style.
///
/// - <see cref="Confirm"/> asks yes/no, e.g. "Delete “Pharmacology”?". For deleting (destructive = true)
///   the button is red and the keyboard starts on Cancel, so pressing Enter can never delete by accident.
/// - <see cref="Prompt"/> asks for a line of text, e.g. a deck name. Enter confirms, Esc cancels.
/// </summary>
public partial class RevDialog : Window
{
    private RevDialog(Window? owner, string title, string? message, string confirmText)
    {
        InitializeComponent();
        Owner = owner;
        TitleText.Text = title;
        MessageText.Text = message ?? "";
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Content = confirmText;
    }

    /// <summary>Asks a yes/no question. Returns true if you clicked the confirm button.</summary>
    public static bool Confirm(Window? owner, string title, string message, string confirmText, bool destructive = false)
    {
        var dialog = new RevDialog(owner, title, message, confirmText);
        if (destructive)
        {
            dialog.ConfirmButton.Style = (Style)dialog.FindResource("DangerPillButton");
            dialog.Loaded += (_, _) => dialog.CancelButton.Focus();
        }
        else
        {
            dialog.ConfirmButton.IsDefault = true;
            dialog.Loaded += (_, _) => dialog.ConfirmButton.Focus();
        }
        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// Offers several choices, e.g. the leech question. The first choice is the safe one (Esc picks it);
    /// the last is shown as the main button. Returns the number of the chosen button (0 = first).
    /// </summary>
    public static int Choose(Window? owner, string title, string message, params string[] choices)
    {
        var dialog = new RevDialog(owner, title, message, "");
        // More than two buttons need more room, so none of them gets cut off.
        if (choices.Length > 2) dialog.Width = 720;
        int chosen = 0;
        dialog.ButtonRow.Children.Clear();
        for (int i = 0; i < choices.Length; i++)
        {
            int number = i;
            bool main = i == choices.Length - 1;
            var button = new Button
            {
                Content = choices[i], MinHeight = 50,
                Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0),
                Style = (Style)dialog.FindResource(main ? "TealPillButton" : "SecondaryPillButton"),
                IsCancel = i == 0,
            };
            button.Click += (_, _) => { chosen = number; dialog.DialogResult = true; };
            dialog.ButtonRow.Children.Add(button);
        }
        dialog.Loaded += (_, _) => ((Button)dialog.ButtonRow.Children[0]).Focus();
        dialog.ShowDialog();
        return chosen;
    }

    /// <summary>Asks for a line of text. Returns it (trimmed), or null if you cancelled.</summary>
    public static string? Prompt(Window? owner, string title, string? message, string initialText,
                                 string confirmText, string hint)
    {
        var dialog = new RevDialog(owner, title, message, confirmText);
        dialog.InputBox.Visibility = Visibility.Visible;
        dialog.InputBox.Tag = hint;
        dialog.InputBox.Text = initialText;
        dialog.ConfirmButton.IsDefault = true;
        dialog.UpdateConfirmEnabled();
        dialog.Loaded += (_, _) =>
        {
            dialog.InputBox.Focus();
            dialog.InputBox.SelectAll();
        };
        return dialog.ShowDialog() == true ? dialog.InputBox.Text.Trim() : null;
    }

    /// <summary>An empty name can't be saved, so the button stays greyed out until you type something.</summary>
    private void UpdateConfirmEnabled() =>
        ConfirmButton.IsEnabled = InputBox.Visibility != Visibility.Visible || InputBox.Text.Trim().Length > 0;

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateConfirmEnabled();

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
