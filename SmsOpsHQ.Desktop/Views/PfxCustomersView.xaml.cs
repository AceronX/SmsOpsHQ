using System.Windows.Controls;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class PfxCustomersView : UserControl
{
    public PfxCustomersView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is PfxCustomersViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);
    }
}
