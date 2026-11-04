using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SudyBrowser.App.ViewModels;

namespace SudyBrowser.App.Views;

public partial class NavigationBarView : UserControl
{
    private OmniboxViewModel? _omnibox;
    private Window? _window;

    // Set while this view edits the box itself, so those edits are not mistaken for typing.
    private bool _isEditingProgrammatically;

    public NavigationBarView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void FocusAddressBar()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && _omnibox is null)
        {
            _omnibox = viewModel.Omnibox;
            _omnibox.InlineCompletionReady += OnInlineCompletionReady;
        }

        // The popup is a separate window and would stay behind when the app moves or loses focus.
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.Deactivated += OnWindowChanged;
            _window.LocationChanged += OnWindowChanged;
            _window.SizeChanged += OnWindowSizeChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_omnibox is not null)
        {
            _omnibox.InlineCompletionReady -= OnInlineCompletionReady;
            _omnibox = null;
        }

        if (_window is not null)
        {
            _window.Deactivated -= OnWindowChanged;
            _window.LocationChanged -= OnWindowChanged;
            _window.SizeChanged -= OnWindowSizeChanged;
            _window = null;
        }
    }

    private void OnWindowChanged(object? sender, EventArgs e) => _omnibox?.Close();

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => _omnibox?.Close();

    private void OnAddressTextChanged(object sender, TextChangedEventArgs e)
    {
        // Only keyboard-driven edits start a query; navigation and tab switches also rewrite the text.
        if (_isEditingProgrammatically || _omnibox is null || !AddressBox.IsKeyboardFocused)
        {
            return;
        }

        var added = e.Changes.Any(change => change.AddedLength > 0);
        var atEnd = AddressBox.SelectionLength == 0 && AddressBox.CaretIndex == AddressBox.Text.Length;
        _omnibox.TextTyped(AddressBox.Text, allowInline: added && atEnd);
    }

    private void OnInlineCompletionReady(object? sender, InlineCompletionEventArgs e)
    {
        // The answer may belong to text the user has already changed or left.
        if (!AddressBox.IsKeyboardFocused
            || AddressBox.Text != e.Typed
            || AddressBox.SelectionLength != 0
            || AddressBox.CaretIndex != e.Typed.Length)
        {
            return;
        }

        EditProgrammatically(() =>
        {
            AddressBox.AppendText(e.Tail);
            AddressBox.Select(e.Typed.Length, e.Tail.Length);
        });
    }

    private void OnAddressPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_omnibox is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down or Key.Up when _omnibox.IsOpen:
                ShowText(_omnibox.MoveSelection(e.Key == Key.Down ? 1 : -1));
                e.Handled = true;
                break;

            case Key.Down when AddressBox.Text.Length > 0:
                // Reopen the list after Escape or a lost focus.
                _omnibox.TextTyped(AddressBox.Text, allowInline: false);
                e.Handled = true;
                break;

            case Key.Escape when _omnibox.IsOpen:
                // Drop any preview or inline completion and go back to exactly what was typed.
                var typed = _omnibox.TypedText;
                _omnibox.Close();
                ShowText(typed);
                e.Handled = true;
                break;
        }
    }

    private void OnAddressLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _omnibox?.Close();

    private void ShowText(string text)
    {
        if (AddressBox.Text == text)
        {
            AddressBox.CaretIndex = text.Length;
            return;
        }

        EditProgrammatically(() =>
        {
            AddressBox.Text = text;
            AddressBox.CaretIndex = text.Length;
        });
    }

    private void EditProgrammatically(Action edit)
    {
        _isEditingProgrammatically = true;
        try
        {
            edit();
        }
        finally
        {
            _isEditingProgrammatically = false;
        }
    }
}
