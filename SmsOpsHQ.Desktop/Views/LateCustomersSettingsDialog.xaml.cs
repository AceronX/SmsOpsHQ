using System.Windows;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.Views;

public partial class LateCustomersSettingsDialog : Window
{
    private readonly LateCustomersQueryService _queryService;
    private readonly string _defaultQuery;

    public string? SavedQuery { get; private set; }

    public LateCustomersSettingsDialog(LateCustomersQueryService queryService)
    {
        InitializeComponent();
        _queryService = queryService;
        _defaultQuery = _queryService.LoadQuery();
        QueryTextBox.Text = _defaultQuery;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        QueryTextBox.Text = _defaultQuery;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string query = QueryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show("Query cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _queryService.SaveQuery(query);
            SavedQuery = query;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save query: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
