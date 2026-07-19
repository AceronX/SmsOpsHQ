using SmsOpsHQ.Desktop.Models;

namespace SmsOpsHQ.Desktop.Services;

public interface IPhonePickerService
{
    string? PickPhone(IReadOnlyList<PhoneChoice> choices, PhonePickerAction action);
}
