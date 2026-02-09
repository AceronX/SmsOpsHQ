using System.Windows.Controls;
using System.Windows.Input;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class InboxView : UserControl
{
    public InboxView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is InboxViewModel vm)
            await vm.LoadInboxCommand.ExecuteAsync(null);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InboxViewModel vm)
            vm.SearchCommand.Execute(null);
    }
}
