using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Rev.Linux.Views;

/// <summary>Every card from every deck, in the card browser (each card shows its deck).</summary>
public partial class AllCardsView : UserControl, IScreen
{
    public AllCardsView()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new RelayCommand(() => Browser.FocusSearch()) });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Escape), Command = new RelayCommand(() => MainWindow.Current.GoBack()) });
        Browser.Setup(() => App.Database.GetCards(), withDeckNames: true);
        Browser.Changed += (_, _) => UpdateEmpty();
        Loaded += (_, _) =>
        {
            UpdateEmpty();
            Browser.FocusSearch();
        };
    }

    public void Refresh()
    {
        Browser.Refresh();
        UpdateEmpty();
    }

    private void UpdateEmpty()
    {
        bool empty = App.Database.GetCards().Count == 0;
        EmptyText.IsVisible = empty;
        Browser.IsVisible = !empty;
    }

    private void Back_Click(object? sender, RoutedEventArgs e) => MainWindow.Current.GoBack();
}
