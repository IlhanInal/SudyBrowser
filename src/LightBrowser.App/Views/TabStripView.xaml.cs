using System.Windows;
using System.Windows.Controls;

namespace SudyBrowser.App.Views;

public partial class TabStripView : UserControl
{
    public TabStripView()
    {
        InitializeComponent();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var reserved = NewTabButton.Width + NewTabButton.Margin.Left;
        TabList.MaxWidth = Math.Max(0, e.NewSize.Width - reserved);
    }
}
