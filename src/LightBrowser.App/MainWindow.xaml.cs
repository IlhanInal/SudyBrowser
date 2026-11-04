using System.Windows;
using System.Windows.Input;
using SudyBrowser.App.ViewModels;

namespace SudyBrowser.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Focusing a control is a view concern, so it lives here rather than in the ViewModel.
        var focusAddress = new RelayCommand(_ => TopBar.FocusAddressBar());
        InputBindings.Add(new KeyBinding(focusAddress, Key.L, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(focusAddress, Key.D, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(focusAddress, Key.F6, ModifierKeys.None));

        StateChanged += OnStateChanged;
        Closed += OnClosed;
    }

    // A maximized borderless window extends past the screen edges by the resize border; pull the content back in.
    private void OnStateChanged(object? sender, EventArgs e) =>
        LayoutRoot.Margin = WindowState == WindowState.Maximized
            ? new Thickness(
                SystemParameters.WindowResizeBorderThickness.Left + SystemParameters.FixedFrameVerticalBorderWidth,
                SystemParameters.WindowResizeBorderThickness.Top + SystemParameters.FixedFrameHorizontalBorderHeight,
                SystemParameters.WindowResizeBorderThickness.Right + SystemParameters.FixedFrameVerticalBorderWidth,
                SystemParameters.WindowResizeBorderThickness.Bottom + SystemParameters.FixedFrameHorizontalBorderHeight)
            : new Thickness(0);

    private void OnClosed(object? sender, EventArgs e)
    {
        StateChanged -= OnStateChanged;
        Closed -= OnClosed;
        _viewModel.Dispose();
    }
}
