using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Views;

public partial class LateCustomersView : UserControl
{
    private static readonly string ColumnOrderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmsOpsHQ", "late_customers_columns.json");

    public LateCustomersView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreColumnOrder();

        if (DataContext is LateCustomersViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);

        AutoFitColumns();
    }

    private void AutoFitColumns()
    {
        foreach (DataGridColumn column in LateGrid.Columns)
            column.Width = new DataGridLength(0, DataGridLengthUnitType.Auto);
        LateGrid.UpdateLayout();
        foreach (DataGridColumn column in LateGrid.Columns)
            column.Width = new DataGridLength(Math.Max(column.ActualWidth, column.MinWidth));
        LateGrid.UpdateLayout();
    }

    internal void OnColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        SaveColumnOrder();
    }

    private void SaveColumnOrder()
    {
        try
        {
            List<string> headers = LateGrid.Columns
                .OrderBy(c => c.DisplayIndex)
                .Select(c => c.Header?.ToString() ?? "")
                .ToList();

            string? dir = Path.GetDirectoryName(ColumnOrderPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(headers);
            File.WriteAllText(ColumnOrderPath, json);
        }
        catch { }
    }

    private void RestoreColumnOrder()
    {
        try
        {
            if (!File.Exists(ColumnOrderPath)) return;
            string json = File.ReadAllText(ColumnOrderPath);
            List<string>? savedOrder = JsonSerializer.Deserialize<List<string>>(json);
            if (savedOrder is null || savedOrder.Count != LateGrid.Columns.Count) return;

            for (int i = 0; i < savedOrder.Count; i++)
            {
                DataGridColumn? col = LateGrid.Columns.FirstOrDefault(
                    c => string.Equals(c.Header?.ToString(), savedOrder[i], StringComparison.Ordinal));
                if (col != null)
                    col.DisplayIndex = i;
            }
        }
        catch { }
    }
}
