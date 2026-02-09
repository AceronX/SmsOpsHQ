using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class ThreadView : UserControl
{
    public ThreadView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ThreadViewModel vm)
        {
            vm.MessagesLoaded += ScrollToBottom;
            await vm.LoadMessagesCommand.ExecuteAsync(null);
            await vm.LoadTemplatesCommand.ExecuteAsync(null);
        }
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() => MessageScroller.ScrollToEnd());
    }

    private void ComposeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ThreadViewModel vm)
        {
            vm.SendMessageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void MediaLink_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is MessageBubbleItem item &&
            DataContext is ThreadViewModel vm)
        {
            vm.OpenMediaViewerCommand.Execute(item);
        }
    }
}
