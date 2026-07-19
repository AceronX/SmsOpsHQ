using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Desktop.Models;
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
    public IReadOnlyList<PhoneChoice> PhoneChoices { get; set; } = Array.Empty<PhoneChoice>();
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
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();
    /// <summary>Backward-compatible display value supplied by older API responses.</summary>
    public string Category { get; set; } = string.Empty;
    public int ForfeitCount { get; set; }
    public int RiskScore { get; set; }
    public string RiskBand { get; set; } = "LOW";
    public string RiskColor { get; set; } = "#34a853";
    public string FullName => $"{FirstName} {LastName}".Trim();
    public IReadOnlyList<string> CategoryChips => Categories.Count > 0
        ? Categories
        : LatePawnCategoryRules.ParseAggregated(Category);
    public string CategoryText => string.Join(" | ", CategoryChips);
    public bool HasJewelry => LatePawnCategoryRules.Contains(CategoryChips, LatePawnCategoryRules.Jewelry);
    public bool HasElectronics => LatePawnCategoryRules.Contains(CategoryChips, LatePawnCategoryRules.Electronics);

    /// <summary>Primary phone formatted for display on cards.</summary>
    public string DisplayPhone =>
        string.IsNullOrEmpty(Phone) ? "" : FormatPhoneForDisplay(Phone);

    /// <summary>Due date for card strip (short year to save width).</summary>
    private string DueDateShort
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DueDate)) return "";
            if (DateTime.TryParse(DueDate, out DateTime dt))
                return dt.ToString("MM/dd/yy");
            return DueDate;
        }
    }

    /// <summary>One line: amount · due · phone (for name row, right side).</summary>
    public string AmountDuePhoneLine
    {
        get
        {
            string line = $"{FormattedAmount} · {DueDateShort}";
            return string.IsNullOrEmpty(DisplayPhone) ? line : $"{line} · {DisplayPhone}";
        }
    }

    /// <summary>Ticket + customer notes for combination (T / C).</summary>
    public string NotesCombinedLine
    {
        get
        {
            bool hasT = !string.IsNullOrWhiteSpace(TicketNotes);
            bool hasC = !string.IsNullOrWhiteSpace(CustomerNotes);
            if (!hasT && !hasC)
                return "";
            if (hasT && hasC)
                return $"T: {TicketNotes} · C: {CustomerNotes}";
            return hasT ? $"T: {TicketNotes}" : $"C: {CustomerNotes}";
        }
    }

    /// <summary>Line 3 of card: items + notes in one string.</summary>
    public string ItemsAndNotesLine
    {
        get
        {
            string it = (Items ?? "").Trim();
            string n = NotesCombinedLine;
            if (string.IsNullOrEmpty(it) && string.IsNullOrEmpty(n))
                return "";
            if (string.IsNullOrEmpty(n))
                return it;
            if (string.IsNullOrEmpty(it))
                return n;
            return $"{it} · {n}";
        }
    }

    public string FormattedAmount => Amount.ToString("C2");
    public string FormattedBalance => Balance.ToString("C2");

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
        return PhoneChoiceBuilder.FormatPhone(digitsOnly);
    }
}

public sealed partial class LateCustomersViewModel : ViewModelBase
{
    public IReadOnlyList<string> CategoryFilters { get; } = new[]
    {
        "All",
        "Jewelry",
        "Electronics",
        "Other"
    };

    private readonly ApiClient _apiClient;
    private readonly LateCustomersQueryService _queryService;
    private readonly IPhoneDialer? _phoneDialer;
    private readonly ISendSmsDialogService _sendSmsDialogService;
    private readonly CustomerQualityQueryService? _qualityQueryService;
    private readonly IPhonePickerService _phonePickerService;
    private List<LateCustomerItem> _allCustomers = new();

    [ObservableProperty]
    private ObservableCollection<LateCustomerItem> _customers = new();

    [ObservableProperty]
    private LateCustomerItem? _selectedLateCustomer;

    [ObservableProperty]
    private CustomerPanelViewModel? _customerPanel;

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
    private string _lastSyncDisplay = "";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategoryFilter = "All";

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedCategoryFilterChanged(string value)
    {
        ApplyFilters();
    }

    public LateCustomersViewModel(
        ApiClient apiClient,
        LateCustomersQueryService queryService,
        IPhoneDialer? phoneDialer,
        ISendSmsDialogService sendSmsDialogService,
        CustomerQualityQueryService? qualityQueryService = null,
        IPhonePickerService? phonePickerService = null)
    {
        _apiClient = apiClient;
        _queryService = queryService;
        _phoneDialer = phoneDialer;
        _sendSmsDialogService = sendSmsDialogService;
        _qualityQueryService = qualityQueryService;
        _phonePickerService = phonePickerService ?? new PhonePickerService();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ClearError();

        try
        {
            await LoadLastSyncAsync();

            string query = _queryService.LoadQuery();
            JsonElement result = await _apiClient.GetLateCustomersAsync(query);
            List<LateCustomerItem> items = new();

            foreach (JsonElement c in result.EnumerateArray())
            {
                List<string> rawPhones = new();
                if (c.TryGetProperty("phones", out JsonElement phonesE) && phonesE.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement pe in phonesE.EnumerateArray())
                    {
                        string? p = pe.GetString();
                        if (!string.IsNullOrWhiteSpace(p))
                            rawPhones.Add(p);
                    }
                }
                if (rawPhones.Count == 0 && c.TryGetProperty("phone", out JsonElement phE))
                {
                    string? single = phE.GetString();
                    if (!string.IsNullOrWhiteSpace(single))
                        rawPhones.Add(single);
                }
                IReadOnlyList<PhoneChoice> phoneChoices = PhoneChoiceBuilder.BuildUnlabeled(rawPhones);
                string primaryPhone = phoneChoices.Count > 0
                    ? phoneChoices[0].PhoneE164
                    : string.Empty;
                IReadOnlyList<string> categories = ReadCategories(c);

                items.Add(new LateCustomerItem
                {
                    CustomerId = c.TryGetProperty("customer_id", out JsonElement cidE) && cidE.ValueKind != JsonValueKind.Null ? cidE.GetInt32() : null,
                    CustomerKey = c.TryGetProperty("customer_key", out JsonElement ckE) ? ckE.GetInt32() : 0,
                    FirstName = c.TryGetProperty("first_name", out JsonElement fnE) ? fnE.GetString() ?? "" : "",
                    LastName = c.TryGetProperty("last_name", out JsonElement lnE) ? lnE.GetString() ?? "" : "",
                    Phone = primaryPhone,
                    PhoneChoices = phoneChoices,
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
                    Categories = categories,
                    Category = string.Join(" | ", categories),
                    ForfeitCount = c.TryGetProperty("forfeit_count", out JsonElement fcE) ? fcE.GetInt32() : 0,
                    RiskScore = c.TryGetProperty("risk_score", out JsonElement rsE) ? rsE.GetInt32() : 0,
                    RiskBand = c.TryGetProperty("risk_band", out JsonElement rbE) ? rbE.GetString() ?? "LOW" : "LOW",
                    RiskColor = c.TryGetProperty("risk_color", out JsonElement rcE) ? rcE.GetString() ?? "#34a853" : "#34a853"
                });
            }

            _allCustomers = items.OrderByDescending(i => i.DaysLate).ToList();
            ApplyFilters();
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

    private async Task LoadLastSyncAsync()
    {
        try
        {
            JsonElement status = await _apiClient.GetSyncStatusAsync();
            if (status.TryGetProperty("last_sync", out JsonElement ls) && ls.ValueKind == JsonValueKind.String)
            {
                string? s = ls.GetString();
                LastSyncDisplay = !string.IsNullOrEmpty(s) ? $"Last Sync: {s}" : "Last Sync: Never";
            }
            else
            {
                LastSyncDisplay = "Last Sync: Never";
            }
        }
        catch
        {
            LastSyncDisplay = "";
        }
    }

    private static IReadOnlyList<string> ReadCategories(JsonElement customer)
    {
        List<string?> rawCategories = new();

        if (customer.TryGetProperty("categories", out JsonElement categoriesElement)
            && categoriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement categoryElement in categoriesElement.EnumerateArray())
            {
                if (categoryElement.ValueKind == JsonValueKind.String)
                    rawCategories.Add(categoryElement.GetString());
            }
        }

        if (rawCategories.Count == 0
            && customer.TryGetProperty("category", out JsonElement legacyCategory)
            && legacyCategory.ValueKind == JsonValueKind.String)
        {
            rawCategories.Add(legacyCategory.GetString());
        }

        return LatePawnCategoryRules.Normalize(rawCategories);
    }

    public static bool MatchesFilters(
        LateCustomerItem customer,
        string? categoryFilter,
        string? searchText)
    {
        bool categoryMatches = categoryFilter switch
        {
            "Jewelry" => customer.HasJewelry,
            "Electronics" => customer.HasElectronics,
            "Other" => !customer.HasJewelry && !customer.HasElectronics,
            _ => true
        };

        if (!categoryMatches || string.IsNullOrWhiteSpace(searchText))
            return categoryMatches;

        string search = searchText.Trim();
        return customer.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.PhoneChoices.Any(phone =>
                phone.PhoneE164.Contains(search, StringComparison.OrdinalIgnoreCase)
                || phone.DisplayPhone.Contains(search, StringComparison.OrdinalIgnoreCase))
            || customer.TicketNo.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.Items.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.CustomerNotes.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.TicketNotes.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.CategoryText.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilters()
    {
        List<LateCustomerItem> filtered = _allCustomers
            .Where(customer => MatchesFilters(customer, SelectedCategoryFilter, SearchText))
            .ToList();
        Customers = new ObservableCollection<LateCustomerItem>(filtered);

        TotalCount = filtered.Count;
        CriticalCount = filtered.Count(c => c.RiskScore >= 70);
        HighCount = filtered.Count(c => c.RiskScore >= 50 && c.RiskScore < 70);
        MediumCount = filtered.Count(c => c.RiskScore >= 30 && c.RiskScore < 50);
        LowCount = filtered.Count(c => c.RiskScore < 30);
    }

    [RelayCommand]
    private async Task OpenCustomerPanelAsync(LateCustomerItem? item)
    {
        if (item is null)
            return;

        string? phone = PickPhone(item, PhonePickerAction.OpenCustomer);
        if (string.IsNullOrEmpty(phone))
            return;

        SelectedLateCustomer = item;

        CustomerPanel ??= new CustomerPanelViewModel(
            _apiClient,
            _phoneDialer,
            _sendSmsDialogService,
            _qualityQueryService,
            _phonePickerService);

        int? key = item.CustomerKey != 0 ? item.CustomerKey : null;
        await CustomerPanel.LoadByPhoneAsync(phone, key);
    }

    [RelayCommand]
    private void SendSms(LateCustomerItem? item)
    {
        if (item is null)
            return;

        string? phone = PickPhone(item, PhonePickerAction.SendSms);
        if (string.IsNullOrEmpty(phone))
            return;

        ClearError();
        _sendSmsDialogService.ShowDialog(prefillPhone: phone);
    }

    [RelayCommand]
    private async Task CallCustomerAsync(LateCustomerItem? item)
    {
        if (item is null)
            return;

        string? phone = PickPhone(item, PhonePickerAction.Call);
        if (string.IsNullOrEmpty(phone))
            return;

        ClearError();

        if (_phoneDialer is null || !_phoneDialer.IsConfigured)
        {
            SetError("XBlue VoIP is not configured. Enable it under Settings → VoIP and set the phone IP.");
            return;
        }

        if (PhoneUtils.GetDialString(phone) is null)
        {
            SetError("No dialable digits in the selected number.");
            return;
        }

        XBlueDialResult result = await _phoneDialer.DialAsync(phone);
        if (!result.Ok)
        {
            SetError(result.StatusCode > 0
                ? $"Call failed (HTTP {result.StatusCode}): {result.Message}"
                : result.Message);
        }
    }

    private string? PickPhone(LateCustomerItem item, PhonePickerAction action)
    {
        if (item.PhoneChoices.Count == 0)
        {
            SetError("No phone number available for this customer.");
            return null;
        }
        return _phonePickerService.PickPhone(item.PhoneChoices, action);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        Window? owner = Application.Current.MainWindow;
        LateCustomersSettingsDialog dialog = new LateCustomersSettingsDialog(_queryService);
        if (owner != null)
            dialog.Owner = owner;

        if (dialog.ShowDialog() == true && dialog.SavedQuery != null)
            _ = LoadCommand.ExecuteAsync(null);
    }
}
