using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Core.Services;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reviews")]
public sealed class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;
    private readonly IReviewRepository _reviewRepo;

    public ReviewsController(IReviewService reviewService, IReviewRepository reviewRepo)
    {
        _reviewService = reviewService;
        _reviewRepo = reviewRepo;
    }

    // POST /api/reviews/send
    [HttpPost("send")]
    public async Task<IActionResult> SendReviewRequest(
        [FromBody] SendReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(request.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        if (string.IsNullOrWhiteSpace(request.CustomerPhone))
            return Problem(statusCode: 400, detail: "Customer phone is required");

        try
        {
            ReviewRequestDto result = await _reviewService.SendReviewRequestAsync(
                request.StoreId, request.CustomerPhone, request.TwilioNumberId, cancellationToken);

            return Ok(result);
        }
        catch (OutboundNumberValidationException ex)
        {
            return Problem(
                title: "Invalid sender number",
                statusCode: StatusCodes.Status400BadRequest,
                detail: ex.Message);
        }
        catch (OutboundSendException ex)
        {
            int statusCode = ex.Attempt.IsMock
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status502BadGateway;
            ProblemDetails problem = new()
            {
                Title = ex.Attempt.IsMock
                    ? "Twilio is in mock mode"
                    : "Review request send failed",
                Status = statusCode,
                Detail = ex.Message
            };
            problem.Extensions["reviewRequestId"] = ex.Attempt.ReviewRequestId;
            problem.Extensions["status"] = ex.Attempt.Status;
            problem.Extensions["providerStatus"] = ex.Attempt.ProviderStatus;
            problem.Extensions["isMock"] = ex.Attempt.IsMock;
            problem.Extensions["errorCode"] = ex.Attempt.ErrorCode;
            problem.Extensions["errorMessage"] = ex.Attempt.ErrorMessage;
            return StatusCode(statusCode, problem);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: 400, detail: ex.Message);
        }
    }

    // GET /api/reviews/readiness?storeId=&twilioNumberId=
    [HttpGet("readiness")]
    public async Task<IActionResult> GetReadiness(
        [FromQuery] int storeId,
        [FromQuery] int? twilioNumberId = null,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        ReviewReadinessDto readiness = await _reviewService.GetReadinessAsync(
            storeId, twilioNumberId, cancellationToken);
        return Ok(readiness);
    }

    // GET /api/reviews/history?storeId=&skip=&take=
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int storeId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        List<ReviewRequest> history = await _reviewRepo.GetRequestHistoryAsync(
            storeId, skip, take, cancellationToken);

        var result = history.Select(r => new ReviewRequestDto
        {
            ReviewRequestId = r.ReviewRequestId,
            PhoneE164 = r.PhoneE164,
            MessageBody = r.MessageBody,
            Status = r.Status,
            SentAt = r.SentAt,
            PlatformName = r.PlatformName ?? string.Empty,
            TwilioSid = r.TwilioSid,
            ProviderStatus = r.ProviderStatus,
            IsMock = string.Equals(r.Status, "Mock", StringComparison.OrdinalIgnoreCase),
            ErrorCode = r.ErrorCode,
            ErrorMessage = r.ErrorMessage,
            DeliveredAt = r.DeliveredAt
        });

        return Ok(result);
    }

    // GET /api/reviews/channels?storeId=
    [HttpGet("channels")]
    public async Task<IActionResult> GetChannels(
        [FromQuery] int storeId,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        List<ReviewChannel> channels = await _reviewRepo.GetChannelsAsync(storeId, cancellationToken);

        var result = channels.Select(c => new ReviewChannelDto
        {
            ReviewChannelId = c.ReviewChannelId,
            StoreId = c.StoreId,
            PlatformName = c.PlatformName,
            ReviewUrl = c.ReviewUrl,
            SortOrder = c.SortOrder,
            IsActive = c.IsActive
        });

        return Ok(result);
    }

    // POST /api/reviews/channels
    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel(
        [FromBody] CreateReviewChannelRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(request.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        if (string.IsNullOrWhiteSpace(request.PlatformName))
            return Problem(statusCode: 400, detail: "Platform name is required");

        if (string.IsNullOrWhiteSpace(request.ReviewUrl))
            return Problem(statusCode: 400, detail: "Review URL is required");

        ReviewChannel channel = await _reviewRepo.CreateChannelAsync(new ReviewChannel
        {
            StoreId = request.StoreId,
            PlatformName = request.PlatformName,
            ReviewUrl = request.ReviewUrl,
            SortOrder = request.SortOrder
        }, cancellationToken);

        return Ok(new ReviewChannelDto
        {
            ReviewChannelId = channel.ReviewChannelId,
            StoreId = channel.StoreId,
            PlatformName = channel.PlatformName,
            ReviewUrl = channel.ReviewUrl,
            SortOrder = channel.SortOrder,
            IsActive = channel.IsActive
        });
    }

    // PUT /api/reviews/channels/{id}
    [HttpPut("channels/{id}")]
    public async Task<IActionResult> UpdateChannel(
        int id,
        [FromBody] UpdateReviewChannelRequest request,
        CancellationToken cancellationToken)
    {
        await _reviewRepo.UpdateChannelAsync(id, request.PlatformName, request.ReviewUrl,
            request.SortOrder, request.IsActive, cancellationToken);

        return Ok(new { status = "updated" });
    }

    // DELETE /api/reviews/channels/{id}
    [HttpDelete("channels/{id}")]
    public async Task<IActionResult> DeleteChannel(
        int id,
        CancellationToken cancellationToken)
    {
        await _reviewRepo.DeleteChannelAsync(id, cancellationToken);
        return Ok(new { status = "deleted" });
    }

    // DELETE /api/reviews/history?storeId=
    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory(
        [FromQuery] int storeId,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        int deleted = await _reviewRepo.ClearRequestHistoryAsync(storeId, cancellationToken);
        return Ok(new { status = "cleared", deleted });
    }
}
