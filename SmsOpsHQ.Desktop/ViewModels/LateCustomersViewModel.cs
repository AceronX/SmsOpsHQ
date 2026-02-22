using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;
using SmsOpsHQ.Desktop.Views;

namespace SmsOpsHQ.Desktop.ViewModels;

public sealed class LateCustomerItem
{
    public int? CustomerId { get; set; }
    public int CustomerKey { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public List<string> PhoneNumbers { get; set; } = new List<string>();
    public int TicketKey { get; set; }
    public int TransNo { get; set; }
    public int TicketNo => TransNo;
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

    public string FormattedAmount => Amount >= 0 ? $"${Amount:F2}" : "$0.00";
    public string FormattedBalance => Balance >= 0 ? $"${Balance:F2}" : "$0.00";

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

    public static string FormatPhoneForDisplay(string digitsOnly)
    {
        if (string.IsNullOrWhiteSpace(digitsOnly)) return "";
        string digits = new string(digitsOnly.Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
            return $"({digits.Substring(0, 3)}) {digits.Substring(3, 3)}-{digits.Substring(6)}";
        return digitsOnly;
    }
}

// Late customers report ViewModel.
public sealed partial class LateCustomersViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly LateCustomersQueryService _queryService;
    private ObservableCollection<LateCustomerItem> _allCustomers = new();

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

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        ApplySearchFilter();
    }

    public LateCustomersViewModel(ApiClient apiClient, LateCustomersQueryService queryService)
    {
        _apiClient = apiClient;
        _queryService = queryService;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ClearError();

        try
        {
            string query = _queryService.LoadQuery();
            JsonElement result = await _apiClient.GetLateCustomersAsync(query);
            ObservableCollection<LateCustomerItem> items = new();

            foreach (JsonElement c in result.EnumerateArray())
            {
                List<string> phones = new List<string>();
                if (c.TryGetProperty("phones", out JsonElement phonesE) && phonesE.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement pe in phonesE.EnumerateArray())
                    {
                        string? p = pe.GetString();
                        if (!string.IsNullOrWhiteSpace(p))
                            phones.Add(p);
                    }
                }
                if (phones.Count == 0 && c.TryGetProperty("phone", out JsonElement phE))
                {
                    string? single = phE.GetString();
                    if (!string.IsNullOrWhiteSpace(single))
                        phones.Add(single);
                }
                string primaryPhone = phones.Count > 0 ? phones[0] : string.Empty;

                items.Add(new LateCustomerItem
                {
                    CustomerId = c.TryGetProperty("customer_id", out JsonElement cidE) && cidE.ValueKind != JsonValueKind.Null ? cidE.GetInt32() : null,
                    CustomerKey = c.TryGetProperty("customer_key", out JsonElement ckE) ? ckE.GetInt32() : 0,
                    FirstName = c.TryGetProperty("first_name", out JsonElement fnE) ? fnE.GetString() ?? "" : "",
                    LastName = c.TryGetProperty("last_name", out JsonElement lnE) ? lnE.GetString() ?? "" : "",
                    Phone = primaryPhone,
                    PhoneNumbers = phones,
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

            _allCustomers = items;
            ApplySearchFilter();
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

    private void ApplySearchFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            Customers = new ObservableCollection<LateCustomerItem>(_allCustomers);
        }
        else
        {
            string searchLower = SearchText.ToLowerInvariant();
            var filtered = _allCustomers.Where(c =>
                c.FullName.ToLowerInvariant().Contains(searchLower) ||
                c.PhoneNumbers.Any(p => p.Contains(searchLower) || LateCustomerItem.FormatPhoneForDisplay(p).Contains(searchLower)) ||
                c.TicketNo.ToString().Contains(searchLower) ||
                c.Items.ToLowerInvariant().Contains(searchLower) ||
                c.CustomerNotes.ToLowerInvariant().Contains(searchLower) ||
                c.TicketNotes.ToLowerInvariant().Contains(searchLower)
            ).ToList();
            Customers = new ObservableCollection<LateCustomerItem>(filtered);
        }

        UpdateStats();
    }

    private void UpdateStats()
    {
        TotalCount = Customers.Count;
        CriticalCount = Customers.Count(c => c.RiskScore >= 70);
        HighCount = Customers.Count(c => c.RiskScore >= 50 && c.RiskScore < 70);
        MediumCount = Customers.Count(c => c.RiskScore >= 30 && c.RiskScore < 50);
        LowCount = Customers.Count(c => c.RiskScore < 30);
    }

    [RelayCommand]
    private async Task SendSmsAsync(LateCustomerItem? item)
    {
        if (item is null) return;
        string? phone = ResolvePhoneForSms(item);
        if (string.IsNullOrEmpty(phone))
        {
            SetError("No phone number available for this customer.");
            return;
        }
        try
        {
            await _apiClient.SendReminderAsync(
                item.TicketKey, item.CustomerKey, phone,
                item.TransNo.ToString(), item.DueDate, item.DaysLate);
        }
        catch (Exception ex)
        {
            SetError($"Reminder failed: {ex.Message}");
        }
    }

    private string? ResolvePhoneForSms(LateCustomerItem item)
    {
        if (item.PhoneNumbers.Count == 0) return null;
        if (item.PhoneNumbers.Count == 1) return item.PhoneNumbers[0];
        var dialog = new PhonePickerDialog(item.PhoneNumbers);
        if (Application.Current.MainWindow is Window owner)
            dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.SelectedPhone : null;
    }

    [RelayCommand]
    private void CallCustomer(LateCustomerItem? item)
    {
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var owner = Application.Current.MainWindow;
        var dialog = new LateCustomersSettingsDialog(_queryService);
        if (owner != null)
            dialog.Owner = owner;

        if (dialog.ShowDialog() == true && dialog.SavedQuery != null)
        {
            // Reload data with the new query so the UI updates immediately
            _ = LoadCommand.ExecuteAsync(null);
        }
    }
}
