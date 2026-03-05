using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class ThreadView : UserControl
{
    private ThreadViewModel? _subscribedVm;

    public ThreadView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeCurrentVm();

        if (e.NewValue is ThreadViewModel vm)
        {
            _subscribedVm = vm;
            vm.MessagesLoaded += ScrollToBottom;
            await vm.LoadMessagesCommand.ExecuteAsync(null);
            await vm.LoadTemplatesCommand.ExecuteAsync(null);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ThreadViewModel vm && _subscribedVm != vm)
        {
            _subscribedVm = vm;
            vm.MessagesLoaded += ScrollToBottom;
            await vm.LoadMessagesCommand.ExecuteAsync(null);
            await vm.LoadTemplatesCommand.ExecuteAsync(null);
        }
    }

    private void UnsubscribeCurrentVm()
    {
        if (_subscribedVm is null) return;
        _subscribedVm.MessagesLoaded -= ScrollToBottom;
        _subscribedVm = null;
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
        if (sender is FrameworkElement { DataContext: MessageBubbleItem item } &&
            DataContext is ThreadViewModel vm)
        {
            vm.OpenMediaViewerCommand.Execute(item);
        }
    }
}
