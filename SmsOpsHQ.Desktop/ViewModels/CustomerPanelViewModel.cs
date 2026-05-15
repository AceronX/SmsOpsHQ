using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

// One quality metric derived from the configurable quality SQL query.
public sealed class QualityMetricItem
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = "0";
    public string Color { get; set; } = "#64748B";
}

public sealed class AmbiguousCandidateRow
{
    public int CustomerKey { get; init; }
    public string DisplayText { get; init; } = string.Empty;
}

// Right-hand panel: resolves conversation phone to customer via phone map, then shows full XPD context.
public sealed partial class CustomerPanelViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private XBlueService? _xblueService;
    private ISendSmsDialogService? _sendSmsDialogService;
    private CustomerQualityQueryService? _qualityQueryService;
    private string _lastPhoneForLookup = string.Empty;

    [ObservableProperty] private int? _customerKey;
    [ObservableProperty] private int? _customerId;
    [ObservableProperty] private bool _isCustomerFound;

    [ObservableProperty] private string _customerName = string.Empty;
    [ObservableProperty] private string _addressLine = string.Empty;
    [ObservableProperty] private string _cityStateZip = string.Empty;
    [ObservableProperty] private string _phoneDisplay = string.Empty;
    [ObservableProperty] private string _customerPhone = string.Empty;
    [ObservableProperty] private string _idInfo = string.Empty;
    [ObservableProperty] private ImageSource? _idPhotoPreview;
    [ObservableProperty] private bool _hasIdPhotoPreview;
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
    [ObservableProperty] private ObservableCollection<QualityMetricItem> _qualityMetrics = new();
    [ObservableProperty] private bool _hasQualityMetrics;

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

    /// <summary>True when lifetime ticket count is 1–2 and CPU/PFX closure history is thin (see alert text).</summary>
    [ObservableProperty] private bool _hasExperienceAlert;
    [ObservableProperty] private string _experienceAlertText = string.Empty;

    [ObservableProperty] private bool _hasDecisionCard;
    [ObservableProperty] private int _decisionScore;
    [ObservableProperty] private string _decisionScoreColor = "#64748B";
    [ObservableProperty] private string _decisionBand = string.Empty;
    [ObservableProperty] private string _decisionBandColor = "#64748B";
    [ObservableProperty] private string _decisionAction = string.Empty;
    [ObservableProperty] private string _decisionPrimaryReason = string.Empty;
    [ObservableProperty] private string _decisionReviewReasons = string.Empty;
    [ObservableProperty] private string _decisionActiveDisplay = string.Empty;
    [ObservableProperty] private string _decisionOverdueDisplay = string.Empty;
    [ObservableProperty] private string _decisionAllTimeDisplay = string.Empty;
    [ObservableProperty] private string _decisionCpuDisplay = string.Empty;
    [ObservableProperty] private string _decisionPfxDisplay = string.Empty;
    [ObservableProperty] private string _decisionEverLateDisplay = string.Empty;
    [ObservableProperty] private string _decisionAvgDaysLateDisplay = string.Empty;
    [ObservableProperty] private string _decisionLateRateDisplay = string.Empty;
    [ObservableProperty] private string _idStatus = string.Empty;
    [ObservableProperty] private string _idStatusColor = "#059669";
    [ObservableProperty] private string _addressStatus = string.Empty;
    [ObservableProperty] private string _addressStatusColor = "#059669";
    [ObservableProperty] private string _contactStatus = string.Empty;
    [ObservableProperty] private string _contactStatusColor = "#059669";

    [ObservableProperty] private ObservableCollection<string> _decisionReviewReasonsList = new();
    [ObservableProperty] private bool _hasDecisionReviewReasons;
    [ObservableProperty] private string _decisionOverdueColor = "#64748B";
    [ObservableProperty] private string _decisionPfxColor = "#64748B";
    [ObservableProperty] private string _decisionEverLateColor = "#059669";
    [ObservableProperty] private string _decisionAvgDaysLateColor = "#059669";
    [ObservableProperty] private string _decisionLateRateColor = "#64748B";
    [ObservableProperty] private string _decisionScoreBg = "#F8FAFC";

    [ObservableProperty] private string _callStatus = string.Empty;

    [ObservableProperty] private bool _isAmbiguous;
    [ObservableProperty] private string _ambiguousMessage = string.Empty;
    [ObservableProperty] private ObservableCollection<AmbiguousCandidateRow> _ambiguousCandidates = new();
    [ObservableProperty] private bool _riskDataSuppressed;
    [ObservableProperty] private string _matchConfidence = string.Empty;
    [ObservableProperty] private string _identityMatchWarning = string.Empty;

    public bool CanClickToCall => _xblueService is not null && _xblueService.IsConfigured;

    public bool ShowFullRiskPanel => IsCustomerFound && !IsAmbiguous && !RiskDataSuppressed;

    partial void OnIsAmbiguousChanged(bool value) => OnPropertyChanged(nameof(ShowFullRiskPanel));

    partial void OnRiskDataSuppressedChanged(bool value) => OnPropertyChanged(nameof(ShowFullRiskPanel));

    partial void OnIsCustomerFoundChanged(bool value) => OnPropertyChanged(nameof(ShowFullRiskPanel));

    public CustomerPanelViewModel(ApiClient apiClient, XBlueService? xblueService = null,
        ISendSmsDialogService? sendSmsDialogService = null, CustomerQualityQueryService? qualityQueryService = null)
    {
        _apiClient = apiClient;
        _xblueService = xblueService;
        _sendSmsDialogService = sendSmsDialogService;
        _qualityQueryService = qualityQueryService;
    }

    public void SetXBlueService(XBlueService service)
    {
        _xblueService = service;
        OnPropertyChanged(nameof(CanClickToCall));
    }

    // Resolve customer by conversation phone (Master Phone Map: ResPhone, BusPhone, Notes) and fill the panel.
    public async Task LoadByPhoneAsync(string phone, int? selectedCustomerKey = null)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            Clear();
            return;
        }

        IsBusy = true;
        ClearError();
        CustomerPhone = phone;
        _lastPhoneForLookup = phone.Trim();

        try
        {
            JsonElement response = await _apiClient.GetCustomerByPhoneAsync(phone, selectedCustomerKey);
            bool found = response.TryGetProperty("found", out JsonElement foundElement) && foundElement.GetBoolean();

            if (!found)
            {
                ShowNotFound(phone);
                return;
            }

            ApplyIdentityFlags(response);

            if (IsAmbiguous)
            {
                IsCustomerFound = false;
                AmbiguousMessage = GetString(response, "error");
                AmbiguousCandidates.Clear();
                if (response.TryGetProperty("candidate_customers", out JsonElement arr))
                {
                    foreach (JsonElement el in arr.EnumerateArray())
                    {
                        int ck = GetInt(el, "customer_key");
                        string name = GetString(el, "name");
                        string src = GetString(el, "phone_match_source");
                        int active = GetInt(el, "active_ticket_count");
                        AmbiguousCandidates.Add(new AmbiguousCandidateRow
                        {
                            CustomerKey = ck,
                            DisplayText = $"{name}  •  #{ck}  •  {src}  •  active tickets: {active}"
                        });
                    }
                }

                ClearRiskOnlySections();
                ClearIdPhotoPreview();
                ClearExperienceAlert();
                CustomerName = "Select a customer";
                AddressLine = "This phone matches more than one XPD profile.";
                CityStateZip = string.Empty;
                PhoneDisplay = phone;
                OnPropertyChanged(nameof(ShowFullRiskPanel));
                return;
            }

            if (!response.TryGetProperty("customer", out JsonElement customerElement) ||
                customerElement.ValueKind != JsonValueKind.Object)
            {
                ShowNotFound(phone);
                return;
            }

            IsCustomerFound = true;
            await PopulateFromResponse(response, customerElement);
            OnPropertyChanged(nameof(ShowFullRiskPanel));
        }
        catch (Exception ex)
        {
            SetError($"Failed to load customer: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanClickToCall));
        }
    }

    // Prefer API phones that actually contain digits; never replace a good thread number with an empty/garbage "phone" field.
    private static string? PickFirstDialablePhone(string? apiPhone, string? resPhone, string? busPhone, string? lookupPhone)
    {
        foreach (string? candidate in new[] { apiPhone, resPhone, busPhone, lookupPhone })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            if (PhoneUtils.GetDialString(candidate) is not null)
                return candidate.Trim();
        }

        return null;
    }

    private void ApplyIdentityFlags(JsonElement response)
    {
        IsAmbiguous = response.TryGetProperty("ambiguous", out JsonElement amb) &&
                    amb.ValueKind == JsonValueKind.True;
        RiskDataSuppressed = response.TryGetProperty("risk_data_suppressed", out JsonElement rs) &&
                             rs.ValueKind == JsonValueKind.True;
        MatchConfidence = GetString(response, "match_confidence");
        IdentityMatchWarning = MatchConfidence == "note_reference_only"
            ? "Phone was found in customer notes only. Confirm client before relying on ticket history."
            : string.Empty;
    }

    [RelayCommand]
    private async Task PickAmbiguousCandidateAsync(int customerKey) =>
        await LoadByPhoneAsync(_lastPhoneForLookup, customerKey);

    private void ClearRiskOnlySections()
    {
        ActiveCount = 0;
        LateCount = 0;
        AllTimeCount = 0;
        HasPaymentHistory = false;
        HasDecisionCard = false;
        HasActiveTickets = false;
        ActiveTickets = new ObservableCollection<ActiveTicketDisplayItem>();
        HasQualityMetrics = false;
        QualityMetrics = new ObservableCollection<QualityMetricItem>();
        LateRateDisplay = string.Empty;
        PaymentStatsDisplay = string.Empty;
        PfxHistoryDisplay = string.Empty;
        WorstLateDisplay = string.Empty;
        IsFirstTimeCustomer = false;
        StatsBorderColor = "#E5E7EB";
        RiskLevel = string.Empty;
        RiskColor = "#64748B";
        ClearExperienceAlert();
    }

    // Blank state when the phone does not match any customer in the phone index.
    private void ShowNotFound(string searchedPhone)
    {
        IsCustomerFound = false;
        IsAmbiguous = false;
        AmbiguousMessage = string.Empty;
        AmbiguousCandidates = new ObservableCollection<AmbiguousCandidateRow>();
        RiskDataSuppressed = false;
        MatchConfidence = string.Empty;
        IdentityMatchWarning = string.Empty;
        CustomerName = "Not in XPD";
        AddressLine = "No address on file";
        CityStateZip = string.Empty;
        PhoneDisplay = string.IsNullOrEmpty(searchedPhone) ? "No phone" : searchedPhone;
        IdInfo = string.Empty;
        ClearIdPhotoPreview();
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
        HasDecisionCard = false;
        ClearExperienceAlert();
    }

    // Map API response (customer, stats, quality, tickets, notes) to display properties.
    private async Task PopulateFromResponse(JsonElement response, JsonElement customerElement)
    {
        CustomerKey = GetIntOrNull(customerElement, "customer_key") ?? GetIntOrNull(customerElement, "key");
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
        string? dialPhone = PickFirstDialablePhone(primaryPhone, resPhone, busPhone, _lastPhoneForLookup);
        if (!string.IsNullOrEmpty(dialPhone))
            CustomerPhone = dialPhone;

        string idNumber = GetString(customerElement, "id_no");
        string idIssueState = GetString(customerElement, "id_issue_state");
        string dateOfBirth = GetString(customerElement, "dob");
        if (dateOfBirth.Contains(' ')) dateOfBirth = dateOfBirth.Split(' ')[0];
        List<string> idParts = new List<string>();
        if (!string.IsNullOrEmpty(idNumber)) idParts.Add("ID: " + idNumber);
        if (!string.IsNullOrEmpty(idIssueState)) idParts.Add("(" + idIssueState + ")");
        if (!string.IsNullOrEmpty(dateOfBirth)) idParts.Add("DOB: " + dateOfBirth);
        IdInfo = string.Join(" ", idParts);

        bool idPhotoAvailable = GetBool(customerElement, "id_photo_available");

        string firstTransaction = GetString(customerElement, "first_transaction");
        if (firstTransaction.Contains(' ')) firstTransaction = firstTransaction.Split(' ')[0];
        SinceDate = !string.IsNullOrEmpty(firstTransaction) ? "Customer since: " + firstTransaction : string.Empty;

        string rawWarning = GetString(customerElement, "warning");
        WarningText = rawWarning.Equals("False", StringComparison.OrdinalIgnoreCase)
                   || rawWarning.Equals("True", StringComparison.OrdinalIgnoreCase)
                   || rawWarning == "0" || rawWarning == "-1" || rawWarning == "1"
            ? string.Empty : rawWarning;

        CustomerNotes = DefaultIfEmpty(GetString(customerElement, "notes"), "No notes.");

        if (!ShowFullRiskPanel)
        {
            ClearRiskOnlySections();
            ClearExperienceAlert();
            TicketNotes = "No ticket notes.";
            ItemNotes = "No item notes.";
            await TryLoadIdPhotoPreviewAsync(CustomerKey, idNumber, idPhotoAvailable);
            return;
        }

        TicketNotes = DefaultIfEmpty(GetString(response, "ticket_notes"), "No ticket notes.");
        ItemNotes = DefaultIfEmpty(GetString(response, "item_notes"), "No item notes.");

        int pfxCountFromStats = 0;
        int cpuCountFromStats = 0;
        if (response.TryGetProperty("stats", out JsonElement statsElement) &&
            statsElement.ValueKind == JsonValueKind.Object)
        {
            ActiveCount = GetInt(statsElement, "active_count");
            LateCount = GetInt(statsElement, "late_count");
            pfxCountFromStats = GetInt(statsElement, "pfx_count");
            cpuCountFromStats = GetInt(statsElement, "cpu_count");
            AllTimeCount = GetInt(statsElement, "all_time_count");

            CpuDisplay = "CPU Count: " + cpuCountFromStats;
            CpuColor = cpuCountFromStats > 0 ? "#D97706" : "#059669";

            string everLateText = LateCount > 0 ? "Yes" : "No";
            PfxLateDisplay = "PFX Count: " + pfxCountFromStats + " | Ever Late: " + everLateText;
            PfxLateColor = (pfxCountFromStats > 0 || LateCount > 0) ? "#D97706" : "#059669";

            ApplyExperienceAlert(AllTimeCount, cpuCountFromStats, pfxCountFromStats);
        }
        else
        {
            ClearExperienceAlert();
        }

        if (response.TryGetProperty("quality", out JsonElement qualityElement) &&
            qualityElement.ValueKind == JsonValueKind.Object)
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
        PopulateDecisionCard(response);

        IsFirstTimeCustomer = AllTimeCount == 1;
        StatsBorderColor = IsFirstTimeCustomer ? "#FBC02D" : "#E5E7EB";

        PopulateActiveTickets(response);

        if (CustomerKey.HasValue)
            await LoadQualityMetricsAsync(CustomerKey.Value);

        await TryLoadIdPhotoPreviewAsync(CustomerKey, idNumber, idPhotoAvailable);
    }

    private void ClearExperienceAlert()
    {
        HasExperienceAlert = false;
        ExperienceAlertText = string.Empty;
    }

    // 1–2 lifetime loans: flag missing CPU paid-up closes and/or missing PFX forfeits so staff spot thin files.
    private void ApplyExperienceAlert(int allTimeCount, int cpuCount, int pfxCount)
    {
        ClearExperienceAlert();
        if (allTimeCount < 1 || allTimeCount > 2)
            return;

        if (cpuCount == 0 && pfxCount == 0)
        {
            HasExperienceAlert = true;
            ExperienceAlertText = "Thin file · no CPU or PFX closes";
            return;
        }

        if (cpuCount == 0)
        {
            HasExperienceAlert = true;
            ExperienceAlertText = "No paid-up (CPU) close yet";
            return;
        }

        if (pfxCount == 0)
        {
            HasExperienceAlert = true;
            ExperienceAlertText = "No PFX forfeits yet";
        }
    }

    private void ClearIdPhotoPreview()
    {
        IdPhotoPreview = null;
        HasIdPhotoPreview = false;
    }

    private async Task TryLoadIdPhotoPreviewAsync(int? key, string idNumber, bool apiSaysAvailable)
    {
        ClearIdPhotoPreview();
        if (key is null || string.IsNullOrWhiteSpace(idNumber) || !apiSaysAvailable)
            return;

        int customerKey = key.Value;
        try
        {
            byte[]? bytes = await _apiClient.GetCustomerIdPhotoBytesAsync(customerKey);
            if (bytes is null || bytes.Length == 0 || CustomerKey != customerKey)
                return;

            void ApplyPreview()
            {
                if (CustomerKey != customerKey)
                    return;
                try
                {
                    using MemoryStream ms = new(bytes);
                    BitmapImage image = new();
                    image.BeginInit();
                    image.StreamSource = ms;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    IdPhotoPreview = image;
                    HasIdPhotoPreview = true;
                }
                catch
                {
                    ClearIdPhotoPreview();
                }
            }

            if (Application.Current?.Dispatcher.CheckAccess() == true)
                ApplyPreview();
            else
                Application.Current?.Dispatcher.Invoke(ApplyPreview);
        }
        catch
        {
            ClearIdPhotoPreview();
        }
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

        if (latePaymentsCount + onTimePaymentsCount == 0)
        {
            LateRateDisplay = "N/A — no redemption history";
            LateRateColor = "#64748B";
        }
        else
        {
            LateRateDisplay = latePaymentRate.ToString("N1") + "% Late Payment Rate";
            if (latePaymentRate > 50 || pfxForfeitedCount >= 3) LateRateColor = "#DC2626";
            else if (latePaymentRate > 20 || pfxForfeitedCount >= 1) LateRateColor = "#D97706";
            else LateRateColor = "#059669";
        }

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

    // Fill decision card fields from the decision_card block in the API response.
    private void PopulateDecisionCard(JsonElement response)
    {
        if (!response.TryGetProperty("decision_card", out JsonElement dc))
        {
            HasDecisionCard = false;
            return;
        }

        HasDecisionCard = true;

        int score = GetInt(dc, "customerScore");
        DecisionScore = score;

        string band = GetString(dc, "scoreBand");
        DecisionBand = band;

        string bandColor = band switch
        {
            "STANDARD" => "#10B981",
            "VERIFY" => "#F59E0B",
            "VERIFY + MANAGER" => "#F97316",
            "MANAGER ONLY" => "#EF4444",
            _ => "#64748B"
        };
        DecisionScoreColor = bandColor;
        DecisionBandColor = bandColor;

        DecisionScoreBg = band switch
        {
            "STANDARD" => "#F0FDF4",
            "VERIFY" => "#FFFBEB",
            "VERIFY + MANAGER" => "#FFF7ED",
            "MANAGER ONLY" => "#FEF2F2",
            _ => "#F8FAFC"
        };

        DecisionAction = GetString(dc, "recommendedAction");
        DecisionPrimaryReason = GetString(dc, "primaryReason");

        string reviewReasons = GetString(dc, "reviewReasons");
        DecisionReviewReasons = reviewReasons;
        var reasonsList = new ObservableCollection<string>();
        if (!string.IsNullOrWhiteSpace(reviewReasons))
        {
            foreach (string r in reviewReasons.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                reasonsList.Add(r);
        }
        DecisionReviewReasonsList = reasonsList;
        HasDecisionReviewReasons = reasonsList.Count > 0;

        int activeTicketCount = GetInt(dc, "activeTickets");
        int overdueCount = GetInt(dc, "overdueActiveTickets");
        int pfxCountDc = GetInt(dc, "pfxCount");

        DecisionActiveDisplay = activeTicketCount.ToString();
        DecisionOverdueDisplay = overdueCount.ToString();
        DecisionAllTimeDisplay = GetInt(dc, "allTimeTickets").ToString();
        DecisionCpuDisplay = GetInt(dc, "cpuCount").ToString();
        DecisionPfxDisplay = pfxCountDc.ToString();

        DecisionOverdueColor = overdueCount > 0 ? "#DC2626" : "#64748B";
        DecisionPfxColor = pfxCountDc > 0 ? "#DC2626" : "#64748B";

        bool everLate = dc.TryGetProperty("everLate", out JsonElement elBool) && elBool.ValueKind == JsonValueKind.True;
        DecisionEverLateDisplay = everLate ? "Yes" : "No";
        DecisionEverLateColor = everLate ? "#D97706" : "#059669";

        double avgDaysLateVal = GetDouble(dc, "avgDaysLate");
        DecisionAvgDaysLateDisplay = avgDaysLateVal.ToString("N1");
        DecisionAvgDaysLateColor = avgDaysLateVal > 30 ? "#DC2626" : avgDaysLateVal > 7 ? "#D97706" : "#059669";

        if (dc.TryGetProperty("latePaymentRate", out JsonElement lrEl) && lrEl.ValueKind == JsonValueKind.Number)
        {
            double lr = lrEl.GetDouble();
            DecisionLateRateDisplay = lr.ToString("N1") + "%";
            DecisionLateRateColor = lr > 50 ? "#DC2626" : lr > 20 ? "#D97706" : "#059669";
        }
        else
        {
            DecisionLateRateDisplay = "N/A";
            DecisionLateRateColor = "#64748B";
        }

        bool missingId = dc.TryGetProperty("flagMissingID", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.True;
        IdStatus = missingId ? "No ID on file" : "ID on file";
        IdStatusColor = missingId ? "#DC2626" : "#059669";

        bool missingAddr = dc.TryGetProperty("flagMissingAddress", out JsonElement addrEl) && addrEl.ValueKind == JsonValueKind.True;
        AddressStatus = missingAddr ? "Address missing" : "Address complete";
        AddressStatusColor = missingAddr ? "#DC2626" : "#059669";

        bool missingContact = dc.TryGetProperty("flagMissingContact", out JsonElement contEl) && contEl.ValueKind == JsonValueKind.True;
        ContactStatus = missingContact ? "No contact info" : "Contact available";
        ContactStatusColor = missingContact ? "#DC2626" : "#059669";
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

    // Use when API does not return quality; derive risk from PFX count and late count from stats.
    private async Task LoadQualityMetricsAsync(int customerKey)
    {
        QualityMetrics.Clear();
        HasQualityMetrics = false;

        if (_qualityQueryService is null || !ShowFullRiskPanel) return;

        try
        {
            JsonElement result = await _apiClient.GetCustomerQualityAsync(customerKey, "default");

            if (result.TryGetProperty("error", out JsonElement errorEl))
                return;

            var items = new List<QualityMetricItem>();
            double avgDaysLate = 0;
            int pfxCount = 0;
            int lateTickets = 0;

            foreach (JsonProperty prop in result.EnumerateObject())
            {
                string label = FormatMetricLabel(prop.Name);
                string value;

                if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    double num = prop.Value.GetDouble();
                    value = num == Math.Floor(num) ? ((int)num).ToString() : num.ToString("N1");

                    if (prop.Name.Contains("avg_days_late", StringComparison.OrdinalIgnoreCase))
                        avgDaysLate = num;
                    else if (prop.Name.Contains("pfx", StringComparison.OrdinalIgnoreCase))
                        pfxCount = (int)num;
                    else if (prop.Name.Contains("late", StringComparison.OrdinalIgnoreCase))
                        lateTickets = (int)num;
                }
                else
                {
                    value = prop.Value.ToString() ?? "0";
                }

                string color = DetermineMetricColor(prop.Name, value);
                items.Add(new QualityMetricItem { Label = label, Value = value, Color = color });
            }

            QualityMetrics = new ObservableCollection<QualityMetricItem>(items);
            HasQualityMetrics = items.Count > 0;

            ApplyQualityRiskLevel(avgDaysLate, pfxCount, lateTickets);
        }
        catch
        {
            HasQualityMetrics = false;
        }
    }

    private static string FormatMetricLabel(string columnName)
    {
        return columnName
            .Replace("_", " ")
            .Replace("  ", " ")
            .Trim()
            .ToUpperInvariant();
    }

    private static string DetermineMetricColor(string columnName, string value)
    {
        if (!double.TryParse(value, out double num))
            return "#64748B";

        string lower = columnName.ToLowerInvariant();

        if (lower.Contains("late") || lower.Contains("overdue"))
        {
            if (num > 30) return "#DC2626";
            if (num > 0) return "#D97706";
            return "#059669";
        }

        if (lower.Contains("pfx") || lower.Contains("forfeit"))
        {
            if (num >= 3) return "#DC2626";
            if (num >= 1) return "#D97706";
            return "#059669";
        }

        if (lower.Contains("closed") || lower.Contains("cpu"))
            return num > 0 ? "#059669" : "#64748B";

        return "#64748B";
    }

    private void ApplyQualityRiskLevel(double avgDaysLate, int pfxCount, int lateTickets)
    {
        if (avgDaysLate > 30 || pfxCount >= 3 || lateTickets >= 3)
        {
            RiskLevel = "High Risk";
            RiskColor = "#DC2626";
        }
        else if (avgDaysLate > 7 || pfxCount >= 1 || lateTickets >= 1)
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
                await LoadByPhoneAsync(CustomerPhone, CustomerKey);
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
    private void SendSms()
    {
        if (_sendSmsDialogService is null || string.IsNullOrEmpty(CustomerPhone))
            return;

        _sendSmsDialogService.ShowDialog(prefillPhone: CustomerPhone);
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
        XBlueDialResult result = await _xblueService.DialAsync(CustomerPhone);
        CallStatus = result.Ok
            ? $"Call sent — {result.Message}"
            : (result.StatusCode > 0
                ? $"Dial failed (HTTP {result.StatusCode}): {result.Message}"
                : result.Message);
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
        ClearIdPhotoPreview();
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
        HasDecisionCard = false;
        DecisionScore = 0;
        DecisionScoreColor = "#64748B";
        DecisionBand = string.Empty;
        DecisionBandColor = "#64748B";
        DecisionAction = string.Empty;
        DecisionPrimaryReason = string.Empty;
        DecisionReviewReasons = string.Empty;
        DecisionActiveDisplay = string.Empty;
        DecisionOverdueDisplay = string.Empty;
        DecisionAllTimeDisplay = string.Empty;
        DecisionCpuDisplay = string.Empty;
        DecisionPfxDisplay = string.Empty;
        DecisionEverLateDisplay = string.Empty;
        DecisionAvgDaysLateDisplay = string.Empty;
        DecisionLateRateDisplay = string.Empty;
        IdStatus = string.Empty;
        IdStatusColor = "#059669";
        AddressStatus = string.Empty;
        AddressStatusColor = "#059669";
        ContactStatus = string.Empty;
        ContactStatusColor = "#059669";
        DecisionReviewReasonsList = new ObservableCollection<string>();
        HasDecisionReviewReasons = false;
        DecisionOverdueColor = "#64748B";
        DecisionPfxColor = "#64748B";
        DecisionEverLateColor = "#059669";
        DecisionAvgDaysLateColor = "#059669";
        DecisionLateRateColor = "#64748B";
        DecisionScoreBg = "#F8FAFC";
        IsAmbiguous = false;
        AmbiguousMessage = string.Empty;
        AmbiguousCandidates = new ObservableCollection<AmbiguousCandidateRow>();
        RiskDataSuppressed = false;
        MatchConfidence = string.Empty;
        IdentityMatchWarning = string.Empty;
        ClearExperienceAlert();
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

    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!element.TryGetProperty(propertyName, out JsonElement valueElement))
            return false;
        return valueElement.ValueKind == JsonValueKind.True;
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
