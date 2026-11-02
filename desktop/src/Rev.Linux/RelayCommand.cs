using System.Windows.Input;

namespace Rev.Linux;

/// <summary>
/// Turns a plain method into a "command", which is what keyboard shortcuts (KeyBinding) need.
/// Example: KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = new RelayCommand(NewDeck) }).
/// </summary>
public sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
