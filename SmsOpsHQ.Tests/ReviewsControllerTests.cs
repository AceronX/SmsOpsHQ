using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.Controllers;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Services;
using Xunit;

namespace SmsOpsHQ.Tests;

public sealed class ReviewsControllerTests
{
    [Fact]
    public async Task SendReviewRequest_PassesSelectedSenderAndReturnsAccepted()
    {
        FakeReviewService service = new()
        {
            SendResult = new ReviewRequestDto { Status = "Accepted", ProviderStatus = "queued" }
        };
        ReviewsController controller = BuildController(service, storeId: 7);

        IActionResult action = await controller.SendReviewRequest(new SendReviewRequest
        {
            StoreId = 7,
            CustomerPhone = "+15552220010",
            TwilioNumberId = 42
        }, CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal("Accepted", Assert.IsType<ReviewRequestDto>(ok.Value).Status);
        Assert.Equal(42, service.LastTwilioNumberId);
    }

    [Fact]
    public async Task SendReviewRequest_MockFailure_ReturnsStructuredProblemDetails()
    {
        ReviewRequestDto attempt = new()
        {
            ReviewRequestId = 12,
            Status = "Mock",
            ProviderStatus = "Mock",
            IsMock = true,
            ErrorCode = "MOCK_MODE",
            ErrorMessage = "Not delivered."
        };
        FakeReviewService service = new()
        {
            SendException = new OutboundSendException("Not delivered.", attempt)
        };
        ReviewsController controller = BuildController(service, storeId: 7);

        IActionResult action = await controller.SendReviewRequest(new SendReviewRequest
        {
            StoreId = 7,
            CustomerPhone = "+15552220011",
            TwilioNumberId = 42
        }, CancellationToken.None);

        ObjectResult result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("Twilio is in mock mode", problem.Title);
        Assert.Equal(true, problem.Extensions["isMock"]);
        Assert.Equal("MOCK_MODE", problem.Extensions["errorCode"]);
        Assert.Equal(12, problem.Extensions["reviewRequestId"]);
    }

    [Fact]
    public async Task GetReadiness_PassesSelectedSenderAndReturnsChecks()
    {
        FakeReviewService service = new()
        {
            Readiness = new ReviewReadinessDto
            {
                Ready = false,
                Checks = new List<ReviewReadinessCheckDto>
                {
                    new() { Code = "twilio", Label = "Twilio live mode", Passed = false, Message = "Mock mode" }
                }
            }
        };
        ReviewsController controller = BuildController(service, storeId: 7);

        IActionResult action = await controller.GetReadiness(7, 42, CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(action);
        Assert.False(Assert.IsType<ReviewReadinessDto>(ok.Value).Ready);
        Assert.Equal(42, service.LastTwilioNumberId);
    }

    private static ReviewsController BuildController(FakeReviewService service, int storeId)
    {
        ClaimsIdentity identity = new(new[]
        {
            new Claim("store_id", storeId.ToString()),
            new Claim("role", "StoreAdmin")
        }, "test");
        return new ReviewsController(service, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private sealed class FakeReviewService : IReviewService
    {
        public ReviewRequestDto SendResult { get; init; } = new();
        public Exception? SendException { get; init; }
        public ReviewReadinessDto Readiness { get; init; } = new();
        public int? LastTwilioNumberId { get; private set; }

        public Task<ReviewRequestDto> SendReviewRequestAsync(
            int storeId,
            string customerPhone,
            int? twilioNumberId = null,
            CancellationToken cancellationToken = default)
        {
            LastTwilioNumberId = twilioNumberId;
            if (SendException is not null)
                throw SendException;
            return Task.FromResult(SendResult);
        }

        public Task<ReviewReadinessDto> GetReadinessAsync(
            int storeId,
            int? twilioNumberId = null,
            CancellationToken cancellationToken = default)
        {
            LastTwilioNumberId = twilioNumberId;
            return Task.FromResult(Readiness);
        }
    }
}
