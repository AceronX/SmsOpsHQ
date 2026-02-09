using SmsOpsHQ.Core.Entities;

namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for Template entities.
public interface ITemplateRepository
{
    // Get a template by ID.
    Task<Template?> GetByIdAsync(int templateId,
        CancellationToken cancellationToken = default);

    // Get all templates for a store.
    Task<List<Template>> GetByStoreAsync(int storeId,
        CancellationToken cancellationToken = default);

    // Create a new template.
    Task<Template> CreateAsync(int storeId, string name, string body,
        string? hotkey, int? createdByUserId,
        CancellationToken cancellationToken = default);

    // Update an existing template.
    Task UpdateAsync(int templateId, string name, string body, string? hotkey,
        CancellationToken cancellationToken = default);

    // Delete a template by ID.
    Task DeleteAsync(int templateId,
        CancellationToken cancellationToken = default);
}
