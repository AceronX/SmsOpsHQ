using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsOpsHQ.Desktop.Services;

namespace SmsOpsHQ.Desktop.ViewModels;

// Represents a ticket in the customer context.
public sealed class TicketDisplayItem
{
    public int TicketKey { get; set; }
    public int? TransNo { get; set; }
    public double Amount { get; set; }
    public double Balance { get; set; }
    public string IssueDate { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public string DateClosed { get; set; } = string.Empty;
    public string HowClosed { get; set; } = string.Empty;
    public string Items { get; set; } = string.Empty;
}

// Customer panel ViewModel: shows customer info, tickets, payment history, risk.
public sealed partial class CustomerPanelViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private XBlueService? _xblueService;

    [ObservableProperty]
    private int _customerId;

    // Customer info
    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _customerPhone = string.Empty;

    [ObservableProperty]
    private int? _xpawnKey;

    [ObservableProperty]
    private string _customerNotes = string.Empty;

    [ObservableProperty]
    private string _customerAddress = string.Empty;

    [ObservableProperty]
    private string _sinceDate = string.Empty;

    // Rollup
    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _lateCount;

    [ObservableProperty]
    private int _pfxCount;

    [ObservableProperty]
    private double _totalBalance;

    [ObservableProperty]
    private int _allTimeCount;

    // Payment history
    [ObservableProperty]
    private double _lateRate;

    [ObservableProperty]
    private string _riskLevel = string.Empty;

    [ObservableProperty]
    private string _riskColor = "#64748B";

    // Tickets
    [ObservableProperty]
    private ObservableCollection<TicketDisplayItem> _activeTickets = new();

    [ObservableProperty]
    private ObservableCollection<TicketDisplayItem> _closedTickets = new();

    [ObservableProperty]
    private string _callStatus = string.Empty;

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

    /// <summary>Populate panel from GetCustomerByPhone response when app CustomerId is not available.</summary>
    public void PopulateFromByPhoneResponse(JsonElement response)
    {
        if (!response.TryGetProperty("customer", out JsonElement cust))
            return;

        string first = cust.TryGetProperty("first_name", out JsonElement fn) ? fn.GetString() ?? "" : "";
        string last = cust.TryGetProperty("last_name", out JsonElement ln) ? ln.GetString() ?? "" : "";
        CustomerName = $"{first} {last}".Trim();
        if (string.IsNullOrEmpty(CustomerName))
            CustomerName = cust.TryGetProperty("phone", out JsonElement ph) ? ph.GetString() ?? "Unknown" : "Unknown";
        CustomerPhone = cust.TryGetProperty("phone", out JsonElement phoneE) ? phoneE.GetString() ?? "" : "";
        CustomerNotes = cust.TryGetProperty("notes", out JsonElement notesE) ? notesE.GetString() ?? "" : "";
        string addr = cust.TryGetProperty("address", out JsonElement addrE) ? addrE.GetString() ?? "" : "";
        string city = cust.TryGetProperty("city", out JsonElement cityE) ? cityE.GetString() ?? "" : "";
        string state = cust.TryGetProperty("state", out JsonElement stateE) ? stateE.GetString() ?? "" : "";
        string zip = cust.TryGetProperty("zip", out JsonElement zipE) ? zipE.GetString() ?? "" : "";
        CustomerAddress = string.Join(", ", new[] { addr, city, state, zip }.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (response.TryGetProperty("stats", out JsonElement stats))
        {
            ActiveCount = stats.TryGetProperty("active_count", out JsonElement ac) ? ac.GetInt32() : 0;
            LateCount = stats.TryGetProperty("late_count", out JsonElement lc) ? lc.GetInt32() : 0;
            PfxCount = stats.TryGetProperty("pfx_count", out JsonElement pc) ? pc.GetInt32() : 0;
            TotalBalance = stats.TryGetProperty("total_balance", out JsonElement tb) ? tb.GetDouble() : 0;
            AllTimeCount = stats.TryGetProperty("all_time_count", out JsonElement at) ? at.GetInt32() : 0;
        }

        if (response.TryGetProperty("quality", out JsonElement quality))
        {
            RiskLevel = quality.TryGetProperty("level", out JsonElement lv) ? lv.GetString() ?? "" : "";
            RiskColor = RiskLevel switch
            {
                "High Risk" => "#DC2626",
                "Medium Risk" => "#F59E0B",
                "Low Risk" => "#3B82F6",
                "Excellent" => "#16A34A",
                _ => "#64748B"
            };
        }

        ActiveTickets = ParseTickets(response, "active_tickets");
        var cpu = ParseTickets(response, "cpu_tickets");
        var pfx = ParseTickets(response, "pfx_tickets");
        var closed = new ObservableCollection<TicketDisplayItem>();
        foreach (var t in cpu) closed.Add(t);
        foreach (var t in pfx) closed.Add(t);
        ClosedTickets = closed;
    }

    [RelayCommand]
    private async Task LoadContextAsync()
    {
        if (CustomerId <= 0) return;

        IsBusy = true;
        ClearError();

        try
        {
            JsonElement result = await _apiClient.GetCustomerContextAsync(CustomerId);

            // Customer
            if (result.TryGetProperty("customer", out JsonElement cust))
            {
                CustomerName = cust.TryGetProperty("name", out JsonElement nameE) ? nameE.GetString() ?? "" : "";
                CustomerPhone = cust.TryGetProperty("phone", out JsonElement phoneE) ? phoneE.GetString() ?? "" : "";
                XpawnKey = cust.TryGetProperty("xpawn_key", out JsonElement keyE) && keyE.ValueKind == JsonValueKind.Number ? keyE.GetInt32() : null;
                CustomerNotes = cust.TryGetProperty("notes", out JsonElement notesE) ? notesE.GetString() ?? "" : "";
                CustomerAddress = cust.TryGetProperty("address", out JsonElement addrE) ? addrE.GetString() ?? "" : "";
                SinceDate = cust.TryGetProperty("since_date", out JsonElement sdE) && sdE.ValueKind == JsonValueKind.String ? sdE.GetString() ?? "" : "";
            }

            // Rollup
            if (result.TryGetProperty("rollup", out JsonElement rollup))
            {
                ActiveCount = rollup.TryGetProperty("active_count", out JsonElement acE) ? acE.GetInt32() : 0;
                LateCount = rollup.TryGetProperty("late_count", out JsonElement lcE) ? lcE.GetInt32() : 0;
                PfxCount = rollup.TryGetProperty("pfx_count", out JsonElement pcE) ? pcE.GetInt32() : 0;
                TotalBalance = rollup.TryGetProperty("total_balance", out JsonElement tbE) ? tbE.GetDouble() : 0;
                AllTimeCount = rollup.TryGetProperty("all_time_count", out JsonElement atE) ? atE.GetInt32() : 0;
            }

            // Payment history
            if (result.TryGetProperty("payment_history", out JsonElement ph))
            {
                LateRate = ph.TryGetProperty("lateRate", out JsonElement lrE) ? lrE.GetDouble() : 0;
                RiskLevel = ph.TryGetProperty("riskLevel", out JsonElement rlE) ? rlE.GetString() ?? "" : "";
                RiskColor = RiskLevel switch
                {
                    "High Risk" => "#DC2626",
                    "Medium Risk" => "#F59E0B",
                    "Low Risk" => "#3B82F6",
                    "Excellent" => "#16A34A",
                    _ => "#64748B"
                };
            }

            // Tickets
            ActiveTickets = ParseTickets(result, "active_tickets");
            ClosedTickets = ParseTickets(result, "closed_tickets");
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

    [RelayCommand]
    private async Task ClickToCallAsync()
    {
        if (_xblueService is null || !_xblueService.IsConfigured)
        {
            CallStatus = "XBlue VoIP not configured. Check Settings > VoIP.";
            return;
        }

        if (string.IsNullOrEmpty(CustomerPhone))
        {
            CallStatus = "No phone number available.";
            return;
        }

        CallStatus = "Dialing...";
        bool success = await _xblueService.DialAsync(CustomerPhone);
        CallStatus = success ? "Call initiated." : "Dial failed. Check XBlue connection.";
    }

    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (CustomerId <= 0) return;

        try
        {
            var request = new SmsOpsHQ.Core.DTOs.UpdateCustomerRequest { Notes = CustomerNotes };
            await _apiClient.UpdateCustomerAsync(CustomerId, request);
        }
        catch (Exception ex)
        {
            SetError($"Save failed: {ex.Message}");
        }
    }

    private static ObservableCollection<TicketDisplayItem> ParseTickets(JsonElement root, string propertyName)
    {
        ObservableCollection<TicketDisplayItem> items = new();
        if (!root.TryGetProperty(propertyName, out JsonElement arr)) return items;

        foreach (JsonElement t in arr.EnumerateArray())
        {
            items.Add(new TicketDisplayItem
            {
                TicketKey = t.TryGetProperty("ticket_key", out JsonElement tkE) ? tkE.GetInt32() : 0,
                TransNo = t.TryGetProperty("trans_no", out JsonElement tnE) && tnE.ValueKind == JsonValueKind.Number ? tnE.GetInt32() : null,
                Amount = t.TryGetProperty("amount", out JsonElement amE) ? amE.GetDouble() : 0,
                Balance = t.TryGetProperty("balance", out JsonElement baE) ? baE.GetDouble() : 0,
                IssueDate = t.TryGetProperty("issue_date", out JsonElement idE) ? idE.GetString() ?? "" : "",
                DueDate = t.TryGetProperty("due_date", out JsonElement ddE) ? ddE.GetString() ?? "" : "",
                DateClosed = t.TryGetProperty("date_closed", out JsonElement dcE) ? dcE.GetString() ?? "" : "",
                HowClosed = t.TryGetProperty("how_closed", out JsonElement hcE) ? hcE.GetString() ?? "" : "",
                Items = t.TryGetProperty("items", out JsonElement itE) ? itE.GetString() ?? "" : ""
            });
        }

        return items;
    }
}
