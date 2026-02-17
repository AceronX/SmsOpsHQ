using System.Windows.Controls;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class ReminderDetailView : UserControl
{
    public ReminderDetailView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ReminderDetailViewModel vm)
            await vm.LoadCustomerContextCommand.ExecuteAsync(null);
    }
}
