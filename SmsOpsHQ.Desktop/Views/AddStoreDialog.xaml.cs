using System.Windows;
using System.Windows.Input;

namespace SmsOpsHQ.Desktop.Views;

public partial class AddStoreDialog : Window
{
    public AddStoreDialog()
    {
        InitializeComponent();
        StoreNameBox.Focus();
    }

    public string StoreName => StoreNameBox?.Text?.Trim() ?? string.Empty;

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddStore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StoreName))
        {
            MessageBox.Show("Please enter a store name.", "Add store", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
        Close();
    }
}
