using System.Windows.Controls;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class ComposeView : UserControl
{
    public ComposeView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ComposeViewModel vm)
            await vm.LoadTemplatesCommand.ExecuteAsync(null);
    }
}
