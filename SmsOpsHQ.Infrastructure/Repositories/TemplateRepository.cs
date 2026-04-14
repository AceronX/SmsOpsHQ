using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;
using SmsOpsHQ.Infrastructure.Persistence.Entities;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core implementation of ITemplateRepository.
// Handles template CRUD scoped to a store.
public sealed class TemplateRepository : ITemplateRepository
{
    private readonly AppDbContext _db;

    public TemplateRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Template?> GetByIdAsync(int templateId,
        CancellationToken cancellationToken = default)
    {
        TemplateEntity? entity = await _db.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TemplateId == templateId, cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task<List<Template>> GetByStoreAsync(int storeId,
        CancellationToken cancellationToken = default)
    {
        List<TemplateEntity> entities = await _db.Templates
            .AsNoTracking()
            .Where(t => t.StoreId == storeId || t.StoreId == 0)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<List<Template>> GetByStoreAndCategoryAsync(int storeId, string category,
        CancellationToken cancellationToken = default)
    {
        List<TemplateEntity> entities = await _db.Templates
            .AsNoTracking()
            .Where(t => (t.StoreId == storeId || t.StoreId == 0) && t.Category == category)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<Template> CreateAsync(int storeId, string name, string body,
        string? hotkey, int? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        return await CreateAsync(storeId, name, body, hotkey, createdByUserId, "General", cancellationToken);
    }

    public async Task<Template> CreateAsync(int storeId, string name, string body,
        string? hotkey, int? createdByUserId, string category,
        CancellationToken cancellationToken = default)
    {
        TemplateEntity entity = new TemplateEntity
        {
            StoreId = storeId,
            Name = name,
            Body = body,
            Hotkey = hotkey,
            CreatedByUserId = createdByUserId,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Templates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDomain(entity);
    }

    public async Task UpdateAsync(int templateId, string name, string body, string? hotkey,
        CancellationToken cancellationToken = default)
    {
        TemplateEntity? entity = await _db.Templates
            .FirstOrDefaultAsync(t => t.TemplateId == templateId, cancellationToken);

        if (entity is null)
            return;

        entity.Name = name;
        entity.Body = body;
        entity.Hotkey = hotkey;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int templateId,
        CancellationToken cancellationToken = default)
    {
        TemplateEntity? entity = await _db.Templates
            .FirstOrDefaultAsync(t => t.TemplateId == templateId, cancellationToken);

        if (entity is null)
            return;

        _db.Templates.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static Template MapToDomain(TemplateEntity entity)
    {
        return new Template
        {
            TemplateId = entity.TemplateId,
            StoreId = entity.StoreId,
            CreatedByUserId = entity.CreatedByUserId,
            Name = entity.Name,
            Body = entity.Body,
            Hotkey = entity.Hotkey,
            Category = entity.Category,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
