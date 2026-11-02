using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Rev.App.Views;

/// <summary>Every card from every deck, in the card browser (each card shows its deck).</summary>
public partial class AllCardsView : UserControl, IScreen
{
    public AllCardsView()
    {
        InitializeComponent();
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Browser.FocusSearch()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MainWindow.Current.GoBack()), Key.Escape, ModifierKeys.None));
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
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Browser.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.GoBack();
}
