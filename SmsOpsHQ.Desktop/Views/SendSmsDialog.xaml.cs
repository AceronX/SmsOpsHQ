using System.Windows;
using SmsOpsHQ.Desktop.ViewModels;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.Views;

public partial class SendSmsDialog : Window
{
    private readonly Action<string?>? _onPhoneForPreview;

    public SendSmsDialog(Action? onSent, Action<string?>? onPhoneForPreview = null)
    {
        InitializeComponent();
        _onPhoneForPreview = onPhoneForPreview;

        ApiClient apiClient = App.ApiClient;
        AppState appState = App.AppState;

        ComposeViewModel vm = new(apiClient, appState,
            onMessageSent: () =>
            {
                _onPhoneForPreview?.Invoke(null);
                onSent?.Invoke();
                DialogResult = true;
                Close();
            },
            onCancelRequested: () =>
            {
                _onPhoneForPreview?.Invoke(null);
                DialogResult = false;
                Close();
            },
            onPhoneForPreview: onPhoneForPreview);

        DataContext = vm;
        Loaded += async (_, _) => await vm.LoadTemplatesCommand.ExecuteAsync(null);
        Closed += (_, _) => _onPhoneForPreview?.Invoke(null);
    }
}
