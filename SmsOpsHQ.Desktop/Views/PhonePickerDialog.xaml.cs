using System.Windows;
using System.Windows.Media;
using SmsOpsHQ.Desktop.Models;

namespace SmsOpsHQ.Desktop.Views;

public partial class PhonePickerDialog : Window
{
    public string? SelectedPhone { get; private set; }
    private readonly IReadOnlyList<PhoneChoice> _choices;

    public PhonePickerDialog(IReadOnlyList<PhoneChoice> choices, PhonePickerAction action)
    {
        InitializeComponent();
        _choices = choices.ToList();

        PhonePickerPresentation presentation = PhonePickerPresentations.For(action);
        Title = presentation.WindowTitle;
        InstructionText.Text = presentation.InstructionText;
        ConfirmLabel.Text = presentation.ConfirmationText;
        ConfirmIcon.Text = presentation.ConfirmationIcon;
        ConfirmButton.Background = (Brush)new BrushConverter().ConvertFromString(
            presentation.ConfirmationColor)!;

        foreach (PhoneChoice choice in _choices)
            PhoneList.Items.Add(choice);
        if (PhoneList.Items.Count > 0)
            PhoneList.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        int idx = PhoneList.SelectedIndex;
        string? selectedPhone = PhoneChoiceBuilder.SelectPhone(_choices, idx);
        if (selectedPhone is not null)
        {
            SelectedPhone = selectedPhone;
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
