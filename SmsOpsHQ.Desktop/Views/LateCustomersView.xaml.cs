using System.Windows;
using System.Windows.Controls;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class LateCustomersView : UserControl
{
    public LateCustomersView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LateCustomersViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);
    }
}
