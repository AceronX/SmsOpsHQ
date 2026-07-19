using SmsOpsHQ.Desktop.Models;
using SmsOpsHQ.Desktop.Services;
using SmsOpsHQ.Desktop.ViewModels;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class PhonePickerActionTests
{
    [Theory]
    [InlineData(PhonePickerAction.SendSms, "Send SMS — Choose Number", "Choose a number for SMS", "Send SMS")]
    [InlineData(PhonePickerAction.Call, "Call — Choose Number", "Choose a number to call", "Call")]
    [InlineData(PhonePickerAction.OpenCustomer, "Open Customer — Choose Number", "Choose a number to open", "Open")]
    [InlineData(PhonePickerAction.SendDirections, "Send Directions — Choose Number", "Choose a number for directions", "Send Directions")]
    [InlineData(PhonePickerAction.RequestReview, "Request Review — Choose Number", "Choose a number for the review request", "Request Review")]
    public void Presentation_IsSpecificToAction(
        PhonePickerAction action,
        string title,
        string instruction,
        string confirmation)
    {
        PhonePickerPresentation presentation = PhonePickerPresentations.For(action);

        Assert.Equal(title, presentation.WindowTitle);
        Assert.Equal(instruction, presentation.InstructionText);
        Assert.Equal(confirmation, presentation.ConfirmationText);
        Assert.False(string.IsNullOrWhiteSpace(presentation.ConfirmationColor));
        Assert.False(string.IsNullOrWhiteSpace(presentation.ConfirmationIcon));
    }

    [Fact]
    public void Presentation_CallAndOpenCustomer_NeverUseSmsWording()
    {
        PhonePickerPresentation call = PhonePickerPresentations.For(PhonePickerAction.Call);
        PhonePickerPresentation open = PhonePickerPresentations.For(PhonePickerAction.OpenCustomer);

        Assert.DoesNotContain("SMS", call.InstructionText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SMS", call.ConfirmationText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SMS", open.InstructionText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SMS", open.ConfirmationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomerChoices_NormalizeLabelAndDeduplicateAcrossSources()
    {
        IReadOnlyList<PhoneChoice> choices = PhoneChoiceBuilder.BuildCustomerChoices(
            "(718) 555-1001",
            "718-555-1002",
            "Call 718.555.1001 or 718 555 1003",
            null);

        Assert.Equal(3, choices.Count);
        Assert.Collection(
            choices,
            home =>
            {
                Assert.Equal("+17185551001", home.PhoneE164);
                Assert.Equal("Home", home.SourceLabel);
                Assert.Equal("(718) 555-1001", home.DisplayPhone);
            },
            work =>
            {
                Assert.Equal("+17185551002", work.PhoneE164);
                Assert.Equal("Work", work.SourceLabel);
            },
            notes =>
            {
                Assert.Equal("+17185551003", notes.PhoneE164);
                Assert.Equal("Notes", notes.SourceLabel);
            });
    }

    [Fact]
    public void Selection_ReturnsExactNormalizedPhone_AndInvalidIndexCancels()
    {
        IReadOnlyList<PhoneChoice> choices = PhoneChoiceBuilder.BuildUnlabeled(new[]
        {
            "(718) 555-2001",
            "718-555-2002"
        });

        Assert.Equal("+17185552002", PhoneChoiceBuilder.SelectPhone(choices, 1));
        Assert.Null(PhoneChoiceBuilder.SelectPhone(choices, -1));
        Assert.Null(PhoneChoiceBuilder.SelectPhone(choices, 2));
    }

    [Theory]
    [InlineData(PhonePickerAction.SendSms)]
    [InlineData(PhonePickerAction.Call)]
    [InlineData(PhonePickerAction.OpenCustomer)]
    [InlineData(PhonePickerAction.SendDirections)]
    [InlineData(PhonePickerAction.RequestReview)]
    public void SingleChoice_ReturnsExactNormalizedPhone_ForEveryAction(PhonePickerAction action)
    {
        IReadOnlyList<PhoneChoice> choices = PhoneChoiceBuilder.BuildUnlabeled(new[]
        {
            "(718) 555-2001"
        });

        string? selected = new PhonePickerService().PickPhone(choices, action);

        Assert.Equal("+17185552001", selected);
    }

    [Fact]
    public async Task LateCustomer_CallAndSms_UseTheExactSelectedPhone()
    {
        RecordingPicker picker = new("+17185553002");
        RecordingDialer dialer = new();
        RecordingSmsDialog sms = new();
        LateCustomersViewModel viewModel = BuildLateCustomersViewModel(picker, dialer, sms);
        LateCustomerItem customer = BuildLateCustomer();

        viewModel.SendSmsCommand.Execute(customer);
        Assert.Equal(PhonePickerAction.SendSms, picker.LastAction);
        Assert.Equal("+17185553002", Assert.Single(sms.Phones));

        await viewModel.CallCustomerCommand.ExecuteAsync(customer);
        Assert.Equal(PhonePickerAction.Call, picker.LastAction);
        Assert.Equal("+17185553002", Assert.Single(dialer.Phones));
    }

    [Fact]
    public async Task CustomerPanel_CallAndSms_UseTheExactSelectedPhone()
    {
        RecordingPicker picker = new("+17185553002");
        RecordingDialer dialer = new();
        RecordingSmsDialog sms = new();
        CustomerPanelViewModel viewModel = new(
            new ApiClient("http://127.0.0.1:1"),
            dialer,
            sms,
            phonePickerService: picker)
        {
            PhoneChoices = new System.Collections.ObjectModel.ObservableCollection<PhoneChoice>(
                BuildLateCustomer().PhoneChoices)
        };

        viewModel.SendSmsCommand.Execute(null);
        Assert.Equal("+17185553002", Assert.Single(sms.Phones));

        await viewModel.ClickToCallCommand.ExecuteAsync(null);
        Assert.Equal("+17185553002", Assert.Single(dialer.Phones));
    }

    [Fact]
    public async Task Cancel_PerformsNoCallOrSmsAction()
    {
        RecordingPicker picker = new(null);
        RecordingDialer dialer = new();
        RecordingSmsDialog sms = new();
        LateCustomersViewModel viewModel = BuildLateCustomersViewModel(picker, dialer, sms);
        LateCustomerItem customer = BuildLateCustomer();

        viewModel.SendSmsCommand.Execute(customer);
        await viewModel.CallCustomerCommand.ExecuteAsync(customer);

        Assert.Empty(sms.Phones);
        Assert.Empty(dialer.Phones);
    }

    private static LateCustomersViewModel BuildLateCustomersViewModel(
        IPhonePickerService picker,
        IPhoneDialer dialer,
        ISendSmsDialogService sms)
    {
        return new LateCustomersViewModel(
            new ApiClient("http://127.0.0.1:1"),
            new LateCustomersQueryService(),
            dialer,
            sms,
            phonePickerService: picker);
    }

    private static LateCustomerItem BuildLateCustomer()
    {
        return new LateCustomerItem
        {
            CustomerKey = 10,
            Phone = "+17185553001",
            PhoneChoices = PhoneChoiceBuilder.BuildUnlabeled(new[]
            {
                "+17185553001",
                "+17185553002"
            })
        };
    }

    private sealed class RecordingPicker(string? result) : IPhonePickerService
    {
        public PhonePickerAction? LastAction { get; private set; }

        public string? PickPhone(IReadOnlyList<PhoneChoice> choices, PhonePickerAction action)
        {
            LastAction = action;
            return result;
        }
    }

    private sealed class RecordingDialer : IPhoneDialer
    {
        public bool IsConfigured => true;
        public List<string> Phones { get; } = new();

        public Task<XBlueDialResult> DialAsync(string phoneNumber)
        {
            Phones.Add(phoneNumber);
            return Task.FromResult(new XBlueDialResult(true, 200, "ok"));
        }
    }

    private sealed class RecordingSmsDialog : ISendSmsDialogService
    {
        public List<string> Phones { get; } = new();

        public void ShowDialog(
            Action? onSent = null,
            Action<string?>? onPhoneForPreview = null,
            string? prefillPhone = null)
        {
            if (prefillPhone is not null)
                Phones.Add(prefillPhone);
        }
    }
}
