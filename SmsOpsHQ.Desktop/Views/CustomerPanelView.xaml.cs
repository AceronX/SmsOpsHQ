using System.Windows.Controls;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class CustomerPanelView : UserControl
{
    public CustomerPanelView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is CustomerPanelViewModel vm)
            await vm.LoadContextCommand.ExecuteAsync(null);
    }
}
