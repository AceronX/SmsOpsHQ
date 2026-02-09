using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Represents one row in the PFX (forfeited) customers report.
public sealed class PfxCustomerItem
{
    public int CustomerKey { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int TicketKey { get; set; }
    public int TransNo { get; set; }
    public string DateClosed { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
}

// PFX customers report ViewModel.
public sealed partial class PfxCustomersViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;

    [ObservableProperty]
    private ObservableCollection<PfxCustomerItem> _customers = new();

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _dayRange = 60;

    public PfxCustomersViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ClearError();

        try
        {
            JsonElement result = await _apiClient.GetPfxCustomersAsync(DayRange);
            ObservableCollection<PfxCustomerItem> items = new();

            foreach (JsonElement c in result.EnumerateArray())
            {
                items.Add(new PfxCustomerItem
                {
                    CustomerKey = c.TryGetProperty("customer_key", out JsonElement ckE) ? ckE.GetInt32() : 0,
                    FirstName = c.TryGetProperty("first_name", out JsonElement fnE) ? fnE.GetString() ?? "" : "",
                    LastName = c.TryGetProperty("last_name", out JsonElement lnE) ? lnE.GetString() ?? "" : "",
                    Phone = c.TryGetProperty("phone", out JsonElement phE) ? phE.GetString() ?? "" : "",
                    TicketKey = c.TryGetProperty("ticket_key", out JsonElement tkE) ? tkE.GetInt32() : 0,
                    TransNo = c.TryGetProperty("trans_no", out JsonElement tnE) ? tnE.GetInt32() : 0,
                    DateClosed = c.TryGetProperty("date_closed", out JsonElement dcE) ? dcE.GetString() ?? "" : "",
                    Amount = c.TryGetProperty("amount", out JsonElement amE) ? amE.GetDouble() : 0
                });
            }

            Customers = items;
            TotalCount = items.Count;
        }
        catch (Exception ex)
        {
            SetError($"Load failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
