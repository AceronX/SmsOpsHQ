using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Represents one row in the late customers report.
public sealed class LateCustomerItem
{
    public int CustomerKey { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int TicketKey { get; set; }
    public int TransNo { get; set; }
    public string DueDate { get; set; } = string.Empty;
    public int DaysLate { get; set; }
    public double Balance { get; set; }
    public string FullName => $"{FirstName} {LastName}".Trim();
}

// Late customers report ViewModel.
public sealed partial class LateCustomersViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;

    [ObservableProperty]
    private ObservableCollection<LateCustomerItem> _customers = new();

    [ObservableProperty]
    private int _totalCount;

    public LateCustomersViewModel(ApiClient apiClient)
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
            JsonElement result = await _apiClient.GetLateCustomersAsync();
            ObservableCollection<LateCustomerItem> items = new();

            foreach (JsonElement c in result.EnumerateArray())
            {
                items.Add(new LateCustomerItem
                {
                    CustomerKey = c.TryGetProperty("customer_key", out JsonElement ckE) ? ckE.GetInt32() : 0,
                    FirstName = c.TryGetProperty("first_name", out JsonElement fnE) ? fnE.GetString() ?? "" : "",
                    LastName = c.TryGetProperty("last_name", out JsonElement lnE) ? lnE.GetString() ?? "" : "",
                    Phone = c.TryGetProperty("phone", out JsonElement phE) ? phE.GetString() ?? "" : "",
                    TicketKey = c.TryGetProperty("ticket_key", out JsonElement tkE) ? tkE.GetInt32() : 0,
                    TransNo = c.TryGetProperty("trans_no", out JsonElement tnE) ? tnE.GetInt32() : 0,
                    DueDate = c.TryGetProperty("due_date", out JsonElement ddE) ? ddE.GetString() ?? "" : "",
                    DaysLate = c.TryGetProperty("days_late", out JsonElement dlE) ? dlE.GetInt32() : 0,
                    Balance = c.TryGetProperty("balance", out JsonElement baE) ? baE.GetDouble() : 0
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

    [RelayCommand]
    private async Task SendReminderAsync(LateCustomerItem item)
    {
        try
        {
            await _apiClient.SendReminderAsync(
                item.TicketKey, item.CustomerKey, item.Phone,
                item.TransNo.ToString(), item.DueDate, item.DaysLate);
        }
        catch (Exception ex)
        {
            SetError($"Reminder failed: {ex.Message}");
        }
    }
}
