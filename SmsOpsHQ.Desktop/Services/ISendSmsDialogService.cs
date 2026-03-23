namespace SmsOpsHQ.Desktop.Services;

/// <summary>Shows a modal dialog for sending a new SMS.</summary>
/// <param name="onSent">Called when a message is sent successfully (e.g. refresh inbox).</param>
/// <param name="onPhoneForPreview">Called when the user enters a phone number so the host can show customer in the right panel. Passed the phone string when valid (10+ digits), or null to clear the panel. Also called with null when the dialog closes.</param>
/// <param name="prefillPhone">Optional phone number to pre-fill in the "To" field.</param>
public interface ISendSmsDialogService
{
    void ShowDialog(Action? onSent = null, Action<string?>? onPhoneForPreview = null, string? prefillPhone = null);
}
