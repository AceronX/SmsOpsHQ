using System.Text.Json;
using SmsOpsHQ.Desktop.Services;
using SmsOpsHQ.Desktop.ViewModels;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class CustomerPanelPhase5Tests
{
    private const string PhoneA = "+17185554001";
    private const string PhoneB = "+17185554002";

    [Fact]
    public async Task RapidSwitch_ACompletesLast_StillDisplaysB()
    {
        FakeCustomerPanelApi api = new();
        TaskCompletionSource<JsonElement> responseA = NewCompletionSource();
        TaskCompletionSource<JsonElement> responseB = NewCompletionSource();
        api.CustomerResponses[PhoneA] = responseA.Task;
        api.CustomerResponses[PhoneB] = responseB.Task;
        CustomerPanelViewModel viewModel = new(api);

        Task loadA = viewModel.LoadByPhoneAsync(PhoneA);
        await api.WaitForCustomerRequestAsync(PhoneA);
        Task loadB = viewModel.LoadByPhoneAsync(PhoneB);
        await api.WaitForCustomerRequestAsync(PhoneB);

        responseB.SetResult(CustomerResponse(2002, "Beta", PhoneB));
        await loadB;
        responseA.SetResult(CustomerResponse(1001, "Alpha", PhoneA));
        await loadA;

        Assert.Equal(2002, viewModel.CustomerKey);
        Assert.Equal("Beta Customer", viewModel.CustomerName);
        Assert.Equal(PhoneB, viewModel.CustomerPhone);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task StaleQualityResult_DoesNotRepaintNewCustomerRisk()
    {
        FakeCustomerPanelApi api = new();
        api.CustomerResponses[PhoneA] = Task.FromResult(
            CustomerResponse(1001, "Alpha", PhoneA, riskDataSuppressed: false));
        api.CustomerResponses[PhoneB] = Task.FromResult(
            CustomerResponse(2002, "Beta", PhoneB));
        TaskCompletionSource<JsonElement> qualityA = NewCompletionSource();
        api.QualityResponses[1001] = qualityA.Task;
        CustomerPanelViewModel viewModel = new(
            api,
            qualityQueryService: new CustomerQualityQueryService());

        Task loadA = viewModel.LoadByPhoneAsync(PhoneA);
        await api.WaitForQualityRequestAsync(1001);
        await viewModel.LoadByPhoneAsync(PhoneB);
        qualityA.SetResult(Parse("{\"avg_days_late\":99,\"pfx_count\":9}"));
        await loadA;

        Assert.Equal(2002, viewModel.CustomerKey);
        Assert.Equal("Beta Customer", viewModel.CustomerName);
        Assert.NotEqual("High Risk", viewModel.RiskLevel);
        Assert.Empty(viewModel.QualityMetrics);
    }

    [Fact]
    public async Task SaveFailure_RetainsTypedNote()
    {
        FakeCustomerPanelApi api = WithImmediateCustomer(1001, "Alpha", PhoneA);
        api.CreateException = new HttpRequestException("database unavailable");
        CustomerPanelViewModel viewModel = new(api);
        await viewModel.LoadByPhoneAsync(PhoneA);
        viewModel.NoteInput = "Do not lose this text";

        await viewModel.AddAppNoteCommand.ExecuteAsync(null);

        Assert.Equal("Do not lose this text", viewModel.NoteInput);
        Assert.Contains("database unavailable", viewModel.ErrorMessage);
        Assert.Empty(viewModel.AppNotes);
    }

    [Fact]
    public async Task SuccessfulSave_AppendsOnlyCurrentCustomer_AndClearsInput()
    {
        FakeCustomerPanelApi api = WithImmediateCustomer(1001, "Alpha", PhoneA);
        api.CreateResult = AppNoteResponse(91, 1001, "Saved for Alpha");
        CustomerPanelViewModel viewModel = new(api);
        await viewModel.LoadByPhoneAsync(PhoneA);
        viewModel.NoteInput = "Saved for Alpha";

        await viewModel.AddAppNoteCommand.ExecuteAsync(null);

        CustomerAppNoteDisplayItem saved = Assert.Single(viewModel.AppNotes);
        Assert.Equal(1001, saved.CustomerKey);
        Assert.Equal("Saved for Alpha", saved.Content);
        Assert.Equal(string.Empty, viewModel.NoteInput);
        Assert.Equal("Saved", viewModel.NoteSaveStatus);
    }

    [Fact]
    public async Task SaveForA_CompletingAfterSwitch_DoesNotAppearUnderB()
    {
        FakeCustomerPanelApi api = WithImmediateCustomer(1001, "Alpha", PhoneA);
        api.CustomerResponses[PhoneB] = Task.FromResult(CustomerResponse(2002, "Beta", PhoneB));
        TaskCompletionSource<JsonElement> saveA = NewCompletionSource();
        api.CreateTask = saveA.Task;
        CustomerPanelViewModel viewModel = new(api);
        await viewModel.LoadByPhoneAsync(PhoneA);
        viewModel.NoteInput = "Alpha only";

        Task saveTask = viewModel.AddAppNoteCommand.ExecuteAsync(null);
        await api.WaitForCreateRequestAsync();
        await viewModel.LoadByPhoneAsync(PhoneB);
        saveA.SetResult(AppNoteResponse(92, 1001, "Alpha only"));
        await saveTask;

        Assert.Equal(2002, viewModel.CustomerKey);
        Assert.Equal("Beta Customer", viewModel.CustomerName);
        Assert.Empty(viewModel.AppNotes);
        Assert.Equal(string.Empty, viewModel.NoteSaveStatus);
    }

    private static FakeCustomerPanelApi WithImmediateCustomer(int key, string firstName, string phone)
    {
        FakeCustomerPanelApi api = new();
        api.CustomerResponses[phone] = Task.FromResult(CustomerResponse(key, firstName, phone));
        return api;
    }

    private static JsonElement CustomerResponse(
        int customerKey,
        string firstName,
        string phone,
        bool riskDataSuppressed = true) =>
        Parse(
            $$"""
            {
              "found": true,
              "ambiguous": false,
              "risk_data_suppressed": {{riskDataSuppressed.ToString().ToLowerInvariant()}},
              "customer_id": {{customerKey}},
              "customer": {
                "customer_key": {{customerKey}},
                "first_name": "{{firstName}}",
                "last_name": "Customer",
                "phone": "{{phone}}",
                "res_phone": "{{phone}}",
                "notes": "Read-only XPD note",
                "id_photo_available": false
              }
            }
            """);

    private static JsonElement AppNoteResponse(
        int noteId,
        int customerKey,
        string content) =>
        Parse(
            $$"""
            {
              "customerAppNoteId": {{noteId}},
              "storeId": 1,
              "customerKey": {{customerKey}},
              "content": "{{content}}",
              "createdByUserId": 1,
              "createdByUsername": "admin",
              "createdAtUtc": "2026-07-19T14:00:00Z"
            }
            """);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static TaskCompletionSource<JsonElement> NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FakeCustomerPanelApi : ICustomerPanelApi
    {
        private readonly Dictionary<string, TaskCompletionSource<bool>> _customerRequests = new();
        private readonly Dictionary<int, TaskCompletionSource<bool>> _qualityRequests = new();
        private readonly TaskCompletionSource<bool> _createRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Dictionary<string, Task<JsonElement>> CustomerResponses { get; } = new();
        public Dictionary<int, Task<JsonElement>> QualityResponses { get; } = new();
        public JsonElement CreateResult { get; set; } = AppNoteResponse(1, 1001, "Saved");
        public Task<JsonElement>? CreateTask { get; set; }
        public Exception? CreateException { get; set; }

        public async Task<JsonElement> GetCustomerByPhoneAsync(
            string phone,
            int? selectedCustomerKey = null,
            CancellationToken cancellationToken = default)
        {
            if (!_customerRequests.TryGetValue(phone, out TaskCompletionSource<bool>? requested))
            {
                requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _customerRequests[phone] = requested;
            }
            requested.TrySetResult(true);

            // Deliberately ignore cancellation so stale-result guards are tested,
            // not just HttpClient cancellation behavior.
            return await CustomerResponses[phone];
        }

        public Task<byte[]?> GetCustomerIdPhotoBytesAsync(
            int customerKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task<JsonElement> GetCustomerQualityAsync(
            int customerKey,
            string qualityMetric = "default",
            CancellationToken cancellationToken = default)
        {
            if (!_qualityRequests.TryGetValue(customerKey, out TaskCompletionSource<bool>? requested))
            {
                requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _qualityRequests[customerKey] = requested;
            }
            requested.TrySetResult(true);
            return QualityResponses.TryGetValue(customerKey, out Task<JsonElement>? response)
                ? response
                : Task.FromResult(Parse("{}"));
        }

        public Task<JsonElement> GetCustomerAppNotesAsync(
            int customerKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Parse("[]"));

        public async Task<JsonElement> CreateCustomerAppNoteAsync(
            int customerKey,
            string content,
            CancellationToken cancellationToken = default)
        {
            _createRequested.TrySetResult(true);
            if (CreateException is not null)
                throw CreateException;
            return CreateTask is null ? CreateResult : await CreateTask;
        }

        public Task WaitForCustomerRequestAsync(string phone)
        {
            if (!_customerRequests.TryGetValue(phone, out TaskCompletionSource<bool>? requested))
            {
                requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _customerRequests[phone] = requested;
            }
            return requested.Task;
        }

        public Task WaitForCreateRequestAsync() => _createRequested.Task;

        public Task WaitForQualityRequestAsync(int customerKey)
        {
            if (!_qualityRequests.TryGetValue(customerKey, out TaskCompletionSource<bool>? requested))
            {
                requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _qualityRequests[customerKey] = requested;
            }
            return requested.Task;
        }
    }
}
