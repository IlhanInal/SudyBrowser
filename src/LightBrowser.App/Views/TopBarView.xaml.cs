using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SudyBrowser.App.Views;

public partial class TopBarView : UserControl
{
    public TopBarView()
    {
        InitializeComponent();
    }

    public void FocusAddressBar() => NavigationBar.FocusAddressBar();

    private Window? Host => Window.GetWindow(this);

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (Host is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Host?.Close();

    // The window is borderless, so empty bar space acts as the title bar.
    private void OnBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || Host is not { } window
            || e.OriginalSource is not DependencyObject source
            || IsInteractive(source))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            window.DragMove();
        }
    }

    private void ToggleMaximize()
    {
        if (Host is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    private static bool IsInteractive(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase or TextBoxBase or ListBoxItem or ScrollBar)
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void NavigationBar_Loaded(object sender, RoutedEventArgs e)
    {

    }
}
