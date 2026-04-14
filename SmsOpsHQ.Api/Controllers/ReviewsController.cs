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
                request.StoreId, request.CustomerPhone, cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: 400, detail: ex.Message);
        }
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

        var result = history.Select(r => new
        {
            r.ReviewRequestId,
            r.PhoneE164,
            r.MessageBody,
            r.Status,
            r.SentAt,
            r.PlatformName
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
