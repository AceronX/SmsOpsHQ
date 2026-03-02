using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Core.Utilities;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// One active pawn ticket shown as a card in the customer panel (amount, due date, late badge).
public sealed class ActiveTicketDisplayItem
{
    public int TicketKey { get; set; }
    public int? TransNo { get; set; }
    public string TicketType { get; set; } = "LOAN";
    public double Amount { get; set; }
    public double Balance { get; set; }
    public double StandardPu { get; set; }
    public double GracePu { get; set; }
    public double RenewAmount { get; set; }
    public string IssueDate { get; set; } = "-";
    public string DueDate { get; set; } = "-";
    public string Items { get; set; } = "No items listed";
    public bool IsLate { get; set; }
    public int DaysLate { get; set; }
    public bool ShowRenewAmount => RenewAmount > 0;
    public string AmountColor => IsLate ? "#DC2626" : "#059669";
    public string BorderColor => IsLate ? "#DC2626" : "#E5E7EB";
    public string CardBackground => IsLate ? "#FEF2F2" : "#FFFFFF";
}

// Right-hand panel: resolves conversation phone to customer via phone map, then shows full XPD context.
public sealed partial class CustomerPanelViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private XBlueService? _xblueService;

    [ObservableProperty] private int? _customerKey;
    [ObservableProperty] private int? _customerId;
    [ObservableProperty] private bool _isCustomerFound;

    [ObservableProperty] private string _customerName = string.Empty;
    [ObservableProperty] private string _addressLine = string.Empty;
    [ObservableProperty] private string _cityStateZip = string.Empty;
    [ObservableProperty] private string _phoneDisplay = string.Empty;
    [ObservableProperty] private string _customerPhone = string.Empty;
    [ObservableProperty] private string _idInfo = string.Empty;
    [ObservableProperty] private string _sinceDate = string.Empty;
    [ObservableProperty] private string _warningText = string.Empty;

    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _lateCount;
    [ObservableProperty] private int _allTimeCount;

    [ObservableProperty] private string _riskLevel = string.Empty;
    [ObservableProperty] private string _riskColor = "#64748B";
    [ObservableProperty] private string _cpuDisplay = string.Empty;
    [ObservableProperty] private string _cpuColor = "#059669";
    [ObservableProperty] private string _pfxLateDisplay = string.Empty;
    [ObservableProperty] private string _pfxLateColor = "#059669";

    [ObservableProperty] private bool _hasPaymentHistory;
    [ObservableProperty] private string _lateRateDisplay = string.Empty;
    [ObservableProperty] private string _lateRateColor = "#059669";
    [ObservableProperty] private string _paymentStatsDisplay = string.Empty;
    [ObservableProperty] private string _pfxHistoryDisplay = string.Empty;
    [ObservableProperty] private string _pfxHistoryColor = "#059669";
    [ObservableProperty] private string _worstLateDisplay = string.Empty;
    [ObservableProperty] private string _worstLateColor = "#059669";

    [ObservableProperty] private string _customerNotes = "No notes.";
    [ObservableProperty] private string _ticketNotes = "No ticket notes.";
    [ObservableProperty] private string _itemNotes = "No item notes.";
    [ObservableProperty] private string _noteInput = string.Empty;
    [ObservableProperty] private bool _isSavingNote;

    [ObservableProperty] private ObservableCollection<ActiveTicketDisplayItem> _activeTickets = new();
    [ObservableProperty] private string _closedTicketsText = string.Empty;
    [ObservableProperty] private bool _hasActiveTickets;
    [ObservableProperty] private bool _hasClosedTickets;

    [ObservableProperty] private bool _isFirstTimeCustomer;
    [ObservableProperty] private string _statsBorderColor = "#E5E7EB";

    [ObservableProperty] private string _callStatus = string.Empty;
    public bool CanClickToCall => _xblueService is not null && _xblueService.IsConfigured;

    public CustomerPanelViewModel(ApiClient apiClient, XBlueService? xblueService = null)
    {
        _apiClient = apiClient;
        _xblueService = xblueService;
    }

    public void SetXBlueService(XBlueService service)
    {
        _xblueService = service;
        OnPropertyChanged(nameof(CanClickToCall));
    }

    // Resolve customer by conversation phone (Master Phone Map: ResPhone, BusPhone, Notes) and fill the panel.
    public async Task LoadByPhoneAsync(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            Clear();
            return;
        }

        IsBusy = true;
        ClearError();
        CustomerPhone = phone;

        try
        {
            JsonElement response = await _apiClient.GetCustomerByPhoneAsync(phone);
            bool found = response.TryGetProperty("found", out JsonElement foundElement) && foundElement.GetBoolean();

            if (!found)
            {
                ShowNotFound(phone);
                return;
            }

            IsCustomerFound = true;
            PopulateFromResponse(response);
        }
        catch (Exception ex)
        {
            SetError($"Failed to load customer: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Blank state when the phone does not match any customer in the phone index.
    private void ShowNotFound(string searchedPhone)
    {
        IsCustomerFound = false;
        CustomerName = "Not in XPD";
        AddressLine = "No address on file";
        CityStateZip = string.Empty;
        PhoneDisplay = string.IsNullOrEmpty(searchedPhone) ? "No phone" : searchedPhone;
        IdInfo = string.Empty;
        SinceDate = string.Empty;
        WarningText = string.Empty;
        ActiveCount = 0;
        LateCount = 0;
        AllTimeCount = 0;
        RiskLevel = "New Customer";
        RiskColor = "#2563EB";
        CpuDisplay = string.Empty;
        CpuColor = "#059669";
        PfxLateDisplay = string.Empty;
        PfxLateColor = "#059669";
        HasPaymentHistory = false;
        CustomerNotes = "No notes.";
        TicketNotes = "No ticket notes.";
        ItemNotes = "No item notes.";
        ActiveTickets = new ObservableCollection<ActiveTicketDisplayItem>();
        ClosedTicketsText = string.Empty;
        HasActiveTickets = false;
        HasClosedTickets = false;
        IsFirstTimeCustomer = false;
        StatsBorderColor = "#E5E7EB";
    }

    // Map API response (customer, stats, quality, tickets, notes) to display properties.
    private void PopulateFromResponse(JsonElement response)
    {
        if (!response.TryGetProperty("customer", out JsonElement customerElement))
            return;

        CustomerKey = GetIntOrNull(customerElement, "key");
        if (response.TryGetProperty("customer_id", out JsonElement customerIdElement) && customerIdElement.ValueKind == JsonValueKind.Number)
            CustomerId = customerIdElement.GetInt32();

        string firstName = GetString(customerElement, "first_name");
        string lastName = GetString(customerElement, "last_name");
        CustomerName = $"{firstName} {lastName}".Trim();
        if (string.IsNullOrEmpty(CustomerName)) CustomerName = "Unknown";

        string streetAddress = GetString(customerElement, "address");
        string city = GetString(customerElement, "city");
        string state = GetString(customerElement, "state");
        string zipCode = GetString(customerElement, "zip");
        AddressLine = string.IsNullOrEmpty(streetAddress) ? "No address" : streetAddress;
        List<string> cityStateParts = new List<string>();
        if (!string.IsNullOrEmpty(city)) cityStateParts.Add(city);
        if (!string.IsNullOrEmpty(state)) cityStateParts.Add(state);
        string cityStateJoined = string.Join(", ", cityStateParts);
        CityStateZip = !string.IsNullOrEmpty(zipCode)
            ? (!string.IsNullOrEmpty(cityStateJoined) ? $"{cityStateJoined} {zipCode}" : zipCode)
            : cityStateJoined;

        // All available nums: ResPhone, BusPhone, and phones parsed from Notes (same sources as phone index).
        string resPhone = GetString(customerElement, "res_phone");
        string busPhone = GetString(customerElement, "bus_phone");
        string notes = GetString(customerElement, "notes");
        var phoneList = new List<string>();
        if (!string.IsNullOrEmpty(resPhone)) phoneList.Add(resPhone);
        if (!string.IsNullOrEmpty(busPhone) && busPhone != resPhone) phoneList.Add("W: " + busPhone);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(resPhone)) _ = seen.Add(PhoneUtils.ExtractLast10Digits(resPhone) ?? resPhone);
        if (!string.IsNullOrEmpty(busPhone)) _ = seen.Add(PhoneUtils.ExtractLast10Digits(busPhone) ?? busPhone);
        foreach (string p in PhoneUtils.ExtractPhonesFromText(notes))
        {
            if (p is not null && seen.Add(p))
                phoneList.Add("Notes: " + p);
        }
        PhoneDisplay = phoneList.Count > 0 ? string.Join(" | ", phoneList) : "No phone";

        string primaryPhone = GetString(customerElement, "phone");
        if (!string.IsNullOrEmpty(primaryPhone)) CustomerPhone = primaryPhone;

        string idNumber = GetString(customerElement, "id_no");
        string idIssueState = GetString(customerElement, "id_issue_state");
        string dateOfBirth = GetString(customerElement, "dob");
        if (dateOfBirth.Contains(' ')) dateOfBirth = dateOfBirth.Split(' ')[0];
        List<string> idParts = new List<string>();
        if (!string.IsNullOrEmpty(idNumber)) idParts.Add("ID: " + idNumber);
        if (!string.IsNullOrEmpty(idIssueState)) idParts.Add("(" + idIssueState + ")");
        if (!string.IsNullOrEmpty(dateOfBirth)) idParts.Add("DOB: " + dateOfBirth);
        IdInfo = string.Join(" ", idParts);

        string firstTransaction = GetString(customerElement, "first_transaction");
        if (firstTransaction.Contains(' ')) firstTransaction = firstTransaction.Split(' ')[0];
        SinceDate = !string.IsNullOrEmpty(firstTransaction) ? "Customer since: " + firstTransaction : string.Empty;

        WarningText = GetString(customerElement, "warning");

        CustomerNotes = DefaultIfEmpty(GetString(customerElement, "notes"), "No notes.");
        TicketNotes = DefaultIfEmpty(GetString(response, "ticket_notes"), "No ticket notes.");
        ItemNotes = DefaultIfEmpty(GetString(response, "item_notes"), "No item notes.");

        int pfxCountFromStats = 0;
        if (response.TryGetProperty("stats", out JsonElement statsElement))
        {
            ActiveCount = GetInt(statsElement, "active_count");
            LateCount = GetInt(statsElement, "late_count");
            pfxCountFromStats = GetInt(statsElement, "pfx_count");
            int cpuCount = GetInt(statsElement, "cpu_count");
            AllTimeCount = GetInt(statsElement, "all_time_count");

            CpuDisplay = "CPU Closures: " + cpuCount;
            CpuColor = cpuCount > 0 ? "#D97706" : "#059669";

            string everLateText = LateCount > 0 ? "Yes" : "No";
            PfxLateDisplay = "PFX Count: " + pfxCountFromStats + " | Ever Late: " + everLateText;
            PfxLateColor = (pfxCountFromStats > 0 || LateCount > 0) ? "#D97706" : "#059669";
        }

        if (response.TryGetProperty("quality", out JsonElement qualityElement))
        {
            RiskLevel = GetString(qualityElement, "level");
            RiskColor = GetString(qualityElement, "color");
            if (string.IsNullOrEmpty(RiskColor)) RiskColor = "#64748B";
        }
        else
        {
            ApplyFallbackRiskLevel(pfxCountFromStats, LateCount);
        }

        PopulatePaymentHistory(response);

        IsFirstTimeCustomer = AllTimeCount == 1;
        StatsBorderColor = IsFirstTimeCustomer ? "#FBC02D" : "#E5E7EB";

        PopulateActiveTickets(response);
        PopulateClosedTickets(response);
    }

    // Fill late rate, PFX sample, and worst late ticket from payment_history (supports camelCase or snake_case).
    private void PopulatePaymentHistory(JsonElement response)
    {
        if (!response.TryGetProperty("payment_history", out JsonElement paymentHistoryElement))
        {
            HasPaymentHistory = false;
            return;
        }

        int totalTicketsCount = GetInt(paymentHistoryElement, "totalTickets");
        if (totalTicketsCount == 0) totalTicketsCount = GetInt(paymentHistoryElement, "total_tickets");

        if (totalTicketsCount <= 0)
        {
            HasPaymentHistory = false;
            return;
        }

        HasPaymentHistory = true;

        double latePaymentRate = GetDouble(paymentHistoryElement, "latePaymentRate");
        if (latePaymentRate == 0) latePaymentRate = GetDouble(paymentHistoryElement, "late_payment_rate");
        int latePaymentsCount = GetInt(paymentHistoryElement, "latePayments");
        if (latePaymentsCount == 0) latePaymentsCount = GetInt(paymentHistoryElement, "late_payments");
        int onTimePaymentsCount = GetInt(paymentHistoryElement, "onTimePayments");
        if (onTimePaymentsCount == 0) onTimePaymentsCount = GetInt(paymentHistoryElement, "on_time_payments");
        int pfxForfeitedCount = GetInt(paymentHistoryElement, "pfxCount");
        if (pfxForfeitedCount == 0) pfxForfeitedCount = GetInt(paymentHistoryElement, "pfx_count");

        LateRateDisplay = latePaymentRate.ToString("N1") + "% Late Payment Rate";
        if (latePaymentRate > 50 || pfxForfeitedCount >= 3) LateRateColor = "#DC2626";
        else if (latePaymentRate > 20 || pfxForfeitedCount >= 1) LateRateColor = "#D97706";
        else LateRateColor = "#059669";

        PaymentStatsDisplay = "Late: " + latePaymentsCount + " | On-Time: " + onTimePaymentsCount;

        if (pfxForfeitedCount > 0)
        {
            List<string> pfxTransNoList = new List<string>();
            if (paymentHistoryElement.TryGetProperty("pfxTicketsSample", out JsonElement pfxSampleElement) ||
                paymentHistoryElement.TryGetProperty("pfx_tickets_sample", out pfxSampleElement))
            {
                foreach (JsonElement pfxTicket in pfxSampleElement.EnumerateArray())
                {
                    int transNo = GetInt(pfxTicket, "transNo");
                    if (transNo == 0) transNo = GetInt(pfxTicket, "trans_no");
                    if (transNo > 0) pfxTransNoList.Add("#" + transNo);
                    if (pfxTransNoList.Count >= 3) break;
                }
            }
            PfxHistoryDisplay = pfxTransNoList.Count > 0
                ? "PFX (Forfeited): " + pfxForfeitedCount + " (" + string.Join(", ", pfxTransNoList) + ")"
                : "PFX (Forfeited): " + pfxForfeitedCount;
            PfxHistoryColor = "#DC2626";
        }
        else
        {
            PfxHistoryDisplay = "PFX (Forfeited): 0";
            PfxHistoryColor = "#059669";
        }

        if (paymentHistoryElement.TryGetProperty("lateTicketsSample", out JsonElement lateSampleElement) ||
            paymentHistoryElement.TryGetProperty("late_tickets_sample", out lateSampleElement))
        {
            if (lateSampleElement.ValueKind == JsonValueKind.Array && lateSampleElement.GetArrayLength() > 0)
            {
                JsonElement worstLateTicket = lateSampleElement[0];
                int daysLateCount = GetInt(worstLateTicket, "daysLate");
                if (daysLateCount == 0) daysLateCount = GetInt(worstLateTicket, "days_late");
                int worstTransNo = GetInt(worstLateTicket, "transNo");
                if (worstTransNo == 0) worstTransNo = GetInt(worstLateTicket, "trans_no");
                WorstLateDisplay = "Worst Late: Ticket #" + worstTransNo + " (" + daysLateCount + " days)";
                WorstLateColor = "#DC2626";
            }
            else
            {
                WorstLateDisplay = "No late payments";
                WorstLateColor = "#059669";
            }
        }
        else
        {
            WorstLateDisplay = "No late payments";
            WorstLateColor = "#059669";
        }
    }

    // Build active ticket cards; compute IsLate and DaysLate from due date vs today.
    private void PopulateActiveTickets(JsonElement response)
    {
        ObservableCollection<ActiveTicketDisplayItem> ticketItems = new ObservableCollection<ActiveTicketDisplayItem>();
        if (response.TryGetProperty("active_tickets", out JsonElement activeTicketsArray))
        {
            DateTime now = DateTime.Now;
            foreach (JsonElement ticketElement in activeTicketsArray.EnumerateArray())
            {
                string dueDateRaw = GetString(ticketElement, "due_date");
                bool ticketIsLate = false;
                int daysLateCount = 0;
                if (!string.IsNullOrEmpty(dueDateRaw))
                {
                    string datePartOnly = dueDateRaw.Contains(' ') ? dueDateRaw.Split(' ')[0] : dueDateRaw;
                    if (DateTime.TryParse(datePartOnly, out DateTime dueDateTime) && dueDateTime < now)
                    {
                        if (dueDateTime.Date < now.Date)
                        {
                            ticketIsLate = true;
                            daysLateCount = (now.Date - dueDateTime.Date).Days;
                        }
                    }
                }

                int ticketTypeCode = GetInt(ticketElement, "type");
                int? transNoValue = null;
                if (ticketElement.TryGetProperty("trans_no", out JsonElement transNoElement) && transNoElement.ValueKind == JsonValueKind.Number)
                    transNoValue = transNoElement.GetInt32();

                ticketItems.Add(new ActiveTicketDisplayItem
                {
                    TicketKey = GetInt(ticketElement, "ticket_key"),
                    TransNo = transNoValue,
                    TicketType = ticketTypeCode == 0 ? "BUY" : "LOAN",
                    Amount = GetDouble(ticketElement, "amount"),
                    Balance = GetDouble(ticketElement, "balance"),
                    StandardPu = GetDouble(ticketElement, "standard_pu"),
                    GracePu = GetDouble(ticketElement, "grace_pu"),
                    RenewAmount = GetDouble(ticketElement, "renew_amount"),
                    IssueDate = FormatDateShort(GetString(ticketElement, "issue_date")),
                    DueDate = FormatDateShort(dueDateRaw),
                    Items = DefaultIfEmpty(GetString(ticketElement, "items"), "No items listed"),
                    IsLate = ticketIsLate,
                    DaysLate = daysLateCount
                });
            }
        }
        ActiveTickets = ticketItems;
        HasActiveTickets = ticketItems.Count > 0;
    }

    // Merge CPU and PFX closed ticket numbers into one comma-separated list.
    private void PopulateClosedTickets(JsonElement response)
    {
        List<string> closedTransNoList = new List<string>();
        if (response.TryGetProperty("cpu_tickets", out JsonElement cpuTicketsArray))
        {
            foreach (JsonElement cpuTicket in cpuTicketsArray.EnumerateArray())
            {
                int transNo = GetInt(cpuTicket, "trans_no");
                if (transNo > 0) closedTransNoList.Add(transNo.ToString());
            }
        }
        if (response.TryGetProperty("pfx_tickets", out JsonElement pfxTicketsArray))
        {
            foreach (JsonElement pfxTicket in pfxTicketsArray.EnumerateArray())
            {
                int transNo = GetInt(pfxTicket, "trans_no");
                if (transNo > 0) closedTransNoList.Add(transNo.ToString());
            }
        }
        ClosedTicketsText = string.Join(", ", closedTransNoList);
        HasClosedTickets = closedTransNoList.Count > 0;
    }

    // Use when API does not return quality; derive risk from PFX count and late count from stats.
    private void ApplyFallbackRiskLevel(int pfxCount, int lateCount)
    {
        if (pfxCount >= 3 || lateCount >= 2)
        {
            RiskLevel = "High Risk";
            RiskColor = "#DC2626";
        }
        else if (pfxCount >= 1 || lateCount >= 1)
        {
            RiskLevel = "Medium Risk";
            RiskColor = "#F59E0B";
        }
        else
        {
            RiskLevel = "Low Risk";
            RiskColor = "#059669";
        }
    }

    // Append note to customer in SQLite (XPD mirror); then reload panel to show updated notes.
    [RelayCommand]
    private async Task AppendNoteXpdAsync()
    {
        string trimmedNote = (NoteInput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedNote) || !CustomerKey.HasValue) return;

        IsSavingNote = true;
        try
        {
            await _apiClient.AppendNoteXpdAsync(CustomerKey.Value, trimmedNote);
            NoteInput = string.Empty;
            if (!string.IsNullOrEmpty(CustomerPhone))
                await LoadByPhoneAsync(CustomerPhone);
        }
        catch (Exception ex)
        {
            SetError("Save failed: " + ex.Message);
        }
        finally
        {
            IsSavingNote = false;
        }
    }

    [RelayCommand]
    private async Task ClickToCallAsync()
    {
        if (_xblueService is null || !_xblueService.IsConfigured)
        {
            CallStatus = "XBlue VoIP not configured.";
            return;
        }
        if (string.IsNullOrEmpty(CustomerPhone))
        {
            CallStatus = "No phone number.";
            return;
        }

        CallStatus = "Dialing...";
        bool dialSuccess = await _xblueService.DialAsync(CustomerPhone);
        CallStatus = dialSuccess ? "Call initiated." : "Dial failed.";
    }

    // Reset all display fields to empty or default so the panel shows no customer.
    private void Clear()
    {
        IsCustomerFound = false;
        CustomerKey = null;
        CustomerId = null;
        CustomerName = string.Empty;
        AddressLine = string.Empty;
        CityStateZip = string.Empty;
        PhoneDisplay = string.Empty;
        CustomerPhone = string.Empty;
        IdInfo = string.Empty;
        SinceDate = string.Empty;
        WarningText = string.Empty;
        ActiveCount = 0;
        LateCount = 0;
        AllTimeCount = 0;
        RiskLevel = string.Empty;
        RiskColor = "#64748B";
        CpuDisplay = string.Empty;
        CpuColor = "#059669";
        PfxLateDisplay = string.Empty;
        PfxLateColor = "#059669";
        HasPaymentHistory = false;
        CustomerNotes = "No notes.";
        TicketNotes = "No ticket notes.";
        ItemNotes = "No item notes.";
        NoteInput = string.Empty;
        ActiveTickets = new ObservableCollection<ActiveTicketDisplayItem>();
        ClosedTicketsText = string.Empty;
        HasActiveTickets = false;
        HasClosedTickets = false;
        IsFirstTimeCustomer = false;
        StatsBorderColor = "#E5E7EB";
    }

    // Safe read of a string property from JSON; returns empty string if missing or not a string.
    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;
        return element.TryGetProperty(propertyName, out JsonElement valueElement) && valueElement.ValueKind == JsonValueKind.String
            ? (valueElement.GetString() ?? string.Empty) : string.Empty;
    }

    // Safe read of an int property from JSON; returns 0 if missing or not a number.
    private static int GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        if (!element.TryGetProperty(propertyName, out JsonElement valueElement)) return 0;
        return valueElement.ValueKind == JsonValueKind.Number ? valueElement.GetInt32() : 0;
    }

    // Safe read of an int property from JSON; returns null if missing or not a number.
    private static int? GetIntOrNull(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(propertyName, out JsonElement valueElement)) return null;
        return valueElement.ValueKind == JsonValueKind.Number ? valueElement.GetInt32() : null;
    }

    // Safe read of a double property from JSON; returns 0 if missing or not a number.
    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        if (!element.TryGetProperty(propertyName, out JsonElement valueElement)) return 0;
        return valueElement.ValueKind == JsonValueKind.Number ? valueElement.GetDouble() : 0;
    }

    // Format date string to MM/dd/yy; use date part only if value includes time.
    private static string FormatDateShort(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString)) return "-";
        string datePart = dateString.Contains(' ') ? dateString.Split(' ')[0] : dateString;
        return DateTime.TryParse(datePart, out DateTime parsedDate) ? parsedDate.ToString("MM/dd/yy") : datePart;
    }

    // Return fallback when value is null or whitespace; otherwise return value.
    private static string DefaultIfEmpty(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
