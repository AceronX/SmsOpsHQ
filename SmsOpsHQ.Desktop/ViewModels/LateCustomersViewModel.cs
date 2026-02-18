using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Represents one row in the late customers report.
public sealed class LateCustomerItem
{
    public int? CustomerId { get; set; }
    public int CustomerKey { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int TicketKey { get; set; }
    public int TransNo { get; set; }
    public int TicketNo => TransNo; // Alias for widget compatibility
    public string DueDate { get; set; } = string.Empty;
    public int DaysLate { get; set; }
    public double Balance { get; set; }
    public double Amount { get; set; }
    public string Items { get; set; } = string.Empty;
    public string ItemNotes { get; set; } = string.Empty;
    public string CustomerNotes { get; set; } = string.Empty;
    public string TicketNotes { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int ForfeitCount { get; set; }
    public int RiskScore { get; set; }
    public string RiskBand { get; set; } = "LOW";
    public string RiskColor { get; set; } = "#34a853";
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string NotesDisplay => !string.IsNullOrWhiteSpace(TicketNotes) ? TicketNotes : ItemNotes;
    
    public string FormattedPhone
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Phone)) return "";
            string digits = new string(Phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 10)
                return $"({digits.Substring(0, 3)}) {digits.Substring(3, 3)}-{digits.Substring(6)}";
            return Phone;
        }
    }
    
    public string FormattedAmount => Amount > 0 ? $"${Amount:F2}" : "$0.00";
    public string FormattedBalance => Balance > 0 ? $"${Balance:F2}" : "$0.00";
    
    public string FormattedDueDate
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DueDate)) return "";
            if (DateTime.TryParse(DueDate, out DateTime dt))
                return dt.ToString("MM/dd/yyyy");
            return DueDate;
        }
    }
}

// Late customers report ViewModel.
public sealed partial class LateCustomersViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;

    [ObservableProperty]
    private ObservableCollection<LateCustomerItem> _customers = new();

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _criticalCount;

    [ObservableProperty]
    private int _highCount;

    [ObservableProperty]
    private int _mediumCount;

    [ObservableProperty]
    private int _lowCount;

    [ObservableProperty]
    private string _statsText = "Loading...";

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
                    CustomerId = c.TryGetProperty("customer_id", out JsonElement cidE) && cidE.ValueKind != JsonValueKind.Null ? cidE.GetInt32() : null,
                    CustomerKey = c.TryGetProperty("customer_key", out JsonElement ckE) ? ckE.GetInt32() : 0,
                    FirstName = c.TryGetProperty("first_name", out JsonElement fnE) ? fnE.GetString() ?? "" : "",
                    LastName = c.TryGetProperty("last_name", out JsonElement lnE) ? lnE.GetString() ?? "" : "",
                    Phone = c.TryGetProperty("phone", out JsonElement phE) ? phE.GetString() ?? "" : "",
                    TicketKey = c.TryGetProperty("ticket_key", out JsonElement tkE) ? tkE.GetInt32() : 0,
                    TransNo = c.TryGetProperty("trans_no", out JsonElement tnE) ? tnE.GetInt32() : 0,
                    DueDate = c.TryGetProperty("due_date", out JsonElement ddE) ? ddE.GetString() ?? "" : "",
                    DaysLate = c.TryGetProperty("days_late", out JsonElement dlE) ? dlE.GetInt32() : 0,
                    Balance = c.TryGetProperty("balance", out JsonElement baE) ? baE.GetDouble() : 0,
                    Amount = c.TryGetProperty("amount", out JsonElement amE) ? amE.GetDouble() : 0,
                    Items = c.TryGetProperty("items", out JsonElement itE) ? itE.GetString() ?? "No items" : "No items",
                    ItemNotes = c.TryGetProperty("item_notes", out JsonElement inE) ? inE.GetString() ?? "" : "",
                    CustomerNotes = c.TryGetProperty("customer_notes", out JsonElement cnE) ? cnE.GetString() ?? "" : "",
                    TicketNotes = c.TryGetProperty("ticket_notes", out JsonElement tnE2) ? tnE2.GetString() ?? "" : "",
                    Category = c.TryGetProperty("category", out JsonElement catE) ? catE.GetString() ?? "" : "",
                    ForfeitCount = c.TryGetProperty("forfeit_count", out JsonElement fcE) ? fcE.GetInt32() : 0,
                    RiskScore = c.TryGetProperty("risk_score", out JsonElement rsE) ? rsE.GetInt32() : 0,
                    RiskBand = c.TryGetProperty("risk_band", out JsonElement rbE) ? rbE.GetString() ?? "LOW" : "LOW",
                    RiskColor = c.TryGetProperty("risk_color", out JsonElement rcE) ? rcE.GetString() ?? "#34a853" : "#34a853"
                });
            }

            Customers = items;
            TotalCount = items.Count;
            CriticalCount = items.Count(c => c.RiskScore >= 70);
            HighCount = items.Count(c => c.RiskScore >= 50 && c.RiskScore < 70);
            MediumCount = items.Count(c => c.RiskScore >= 30 && c.RiskScore < 50);
            LowCount = items.Count(c => c.RiskScore < 30);
            StatsText = $"Total: {TotalCount} | 🔴 Critical: {CriticalCount} | 🟠 High: {HighCount} | 🟡 Medium: {MediumCount} | 🟢 Low: {LowCount}";
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
