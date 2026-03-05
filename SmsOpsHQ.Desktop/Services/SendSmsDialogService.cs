using System.Windows;

namespace SmsOpsHQ.Desktop.Services;

public sealed class SendSmsDialogService : ISendSmsDialogService
{
    public void ShowDialog(Action? onSent = null, Action<string?>? onPhoneForPreview = null)
    {
        Window? owner = Application.Current?.MainWindow;

        var dialog = new Views.SendSmsDialog(onSent, onPhoneForPreview);
        if (owner != null)
            dialog.Owner = owner;

        dialog.ShowDialog();
    }
}
