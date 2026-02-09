using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Core.DTOs;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;

namespace SmsOpsHQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class TemplatesController : ControllerBase
{
    private readonly ITemplateRepository _templateRepo;

    public TemplatesController(ITemplateRepository templateRepo)
    {
        _templateRepo = templateRepo;
    }

    // GET /api/templates?store_id=1
    // Lists all templates for a store. HQ users see global templates.
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(
        [FromQuery(Name = "store_id")] int storeId,
        CancellationToken cancellationToken)
    {
        if (!User.CanAccessStore(storeId))
            return Problem(statusCode: 403, detail: "Not authorized for this store");

        List<Template> templates = await _templateRepo.GetByStoreAsync(storeId, cancellationToken);

        List<object> result = templates.Select(t => (object)new
        {
            id = t.TemplateId,
            name = t.Name,
            body = t.Body,
            hotkey = t.Hotkey,
            is_global = t.StoreId == 0
        }).ToList();

        return Ok(result);
    }

    // POST /api/templates
    // Creates a new message template. HQ users create global templates.
    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] TemplateCreateRequest request,
        CancellationToken cancellationToken)
    {
        // HQ users create templates under StoreId=1 (HQ store); store users under their store.
        int? effectiveStoreId = User.IsHqUser() ? 1 : User.GetStoreId();
        if (effectiveStoreId is null)
            return Problem(statusCode: 403, detail: "No store assigned");

        int userId = User.GetUserId();

        Template template = await _templateRepo.CreateAsync(
            effectiveStoreId.Value, request.Name, request.Body,
            null, userId, cancellationToken);

        return Ok(new
        {
            id = template.TemplateId,
            name = template.Name,
            body = template.Body
        });
    }

    // PUT /api/templates/{templateId}
    // Updates an existing template's name and body.
    [HttpPut("templates/{templateId}")]
    public async Task<IActionResult> UpdateTemplate(
        int templateId,
        [FromBody] TemplateCreateRequest request,
        CancellationToken cancellationToken)
    {
        // Verify the template exists and the user has access to its store.
        Template? existing = await _templateRepo.GetByIdAsync(templateId, cancellationToken);
        if (existing is null)
            return Problem(statusCode: 404, detail: "Template not found");

        if (!User.CanAccessStore(existing.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized to update this template");

        await _templateRepo.UpdateAsync(templateId, request.Name, request.Body, null, cancellationToken);

        return Ok(new
        {
            id = templateId,
            name = request.Name,
            body = request.Body
        });
    }

    // DELETE /api/templates/{templateId}
    // Deletes a template by ID.
    [HttpDelete("templates/{templateId}")]
    public async Task<IActionResult> DeleteTemplate(
        int templateId,
        CancellationToken cancellationToken)
    {
        // Verify the template exists and the user has access to its store.
        Template? existing = await _templateRepo.GetByIdAsync(templateId, cancellationToken);
        if (existing is null)
            return Problem(statusCode: 404, detail: "Template not found");

        if (!User.CanAccessStore(existing.StoreId))
            return Problem(statusCode: 403, detail: "Not authorized to delete this template");

        await _templateRepo.DeleteAsync(templateId, cancellationToken);
        return Ok(new { status = "deleted" });
    }
}
