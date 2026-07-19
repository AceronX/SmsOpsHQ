using System.Windows;
using SmsOpsHQ.Desktop.Models;
using SmsOpsHQ.Desktop.Views;

namespace SmsOpsHQ.Desktop.Services;

public sealed class PhonePickerService : IPhonePickerService
{
    public string? PickPhone(IReadOnlyList<PhoneChoice> choices, PhonePickerAction action)
    {
        if (choices.Count == 0)
            return null;
        if (choices.Count == 1)
            return choices[0].PhoneE164;

        PhonePickerDialog dialog = new(choices, action);
        if (Application.Current?.MainWindow is Window owner)
            dialog.Owner = owner;

        return dialog.ShowDialog() == true ? dialog.SelectedPhone : null;
    }
}
