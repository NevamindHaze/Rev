using System.Windows.Input;

namespace Rev.App;

/// <summary>
/// Turns a plain method into a WPF "command", which is what keyboard shortcuts
/// (KeyBinding) need. Example: new KeyBinding(new RelayCommand(NewDeck), Key.N, ModifierKeys.Control).
/// </summary>
public sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
