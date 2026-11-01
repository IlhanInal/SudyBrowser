using System.Windows.Controls;

namespace LightBrowser.App.Views;

public partial class NavigationBarView : UserControl
{
    public NavigationBarView()
    {
        InitializeComponent();
    }

    public void FocusAddressBar()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }
}
