using System.Windows.Controls;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class RemindersView : UserControl
{
    public RemindersView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is RemindersViewModel vm)
            await vm.LoadRemindersCommand.ExecuteAsync(null);
    }
}
