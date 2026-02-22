using System.Collections.Generic;
using System.Windows;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class PhonePickerDialog : Window
{
    public string? SelectedPhone { get; private set; }
    private readonly List<string> _phoneNumbers;

    public PhonePickerDialog(List<string> phoneNumbers)
    {
        InitializeComponent();
        _phoneNumbers = new List<string>(phoneNumbers);
        foreach (string p in _phoneNumbers)
            PhoneList.Items.Add(LateCustomerItem.FormatPhoneForDisplay(p));
        if (PhoneList.Items.Count > 0)
            PhoneList.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        int idx = PhoneList.SelectedIndex;
        if (idx >= 0 && idx < _phoneNumbers.Count)
        {
            SelectedPhone = _phoneNumbers[idx];
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
