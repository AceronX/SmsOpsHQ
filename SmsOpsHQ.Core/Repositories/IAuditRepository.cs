namespace SmsOpsHQ.Core.Repositories;

// Data-access contract for audit log entries.
public interface IAuditRepository
{
    // Write an audit log entry.
    Task LogAsync(int? userId, int? storeId, string action,
        string? entityType, int? entityId, string? details, string? ipAddress,
        CancellationToken cancellationToken = default);
}
