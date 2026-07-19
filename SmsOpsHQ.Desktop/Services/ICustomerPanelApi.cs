using System.Text.Json;

namespace SmsOpsHQ.Desktop.Services;

public interface ICustomerPanelApi
{
    Task<JsonElement> GetCustomerByPhoneAsync(
        string phone,
        int? selectedCustomerKey = null,
        CancellationToken cancellationToken = default);

    Task<byte[]?> GetCustomerIdPhotoBytesAsync(
        int customerKey,
        CancellationToken cancellationToken = default);

    Task<JsonElement> GetCustomerQualityAsync(
        int customerKey,
        string qualityMetric = "default",
        CancellationToken cancellationToken = default);

    Task<JsonElement> GetCustomerAppNotesAsync(
        int customerKey,
        CancellationToken cancellationToken = default);

    Task<JsonElement> CreateCustomerAppNoteAsync(
        int customerKey,
        string content,
        CancellationToken cancellationToken = default);
}
