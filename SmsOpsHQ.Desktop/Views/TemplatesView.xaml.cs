using System.Windows.Controls;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class TemplatesView : UserControl
{
    public TemplatesView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is TemplatesViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);
    }
}
