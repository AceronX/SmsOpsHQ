using System.Windows;

namespace SmsOpsHQ.Desktop.Services;

public sealed class SendSmsDialogService : ISendSmsDialogService
{
    public void ShowDialog(Action? onSent = null, Action<string?>? onPhoneForPreview = null, string? prefillPhone = null)
    {
        Window? owner = Application.Current?.MainWindow;

        var dialog = new Views.SendSmsDialog(onSent, onPhoneForPreview, prefillPhone);
        if (owner != null)
            dialog.Owner = owner;

        dialog.ShowDialog();
    }
}
