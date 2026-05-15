using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Api.Extensions;
using SmsOpsHQ.Api.Support;

namespace SmsOpsHQ.Api.Controllers;

public sealed partial class CustomersController
{
    // GET /api/customer/id-photo?customerKey=123 — optional scan from Xpd:IdPhotosDirectory/{key}.jpg|png|...
    [HttpGet("customer/id-photo")]
    public async Task<IActionResult> GetCustomerIdPhoto(
        [FromQuery] int customerKey,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessCustomerKeyForIdPhotoAsync(customerKey, cancellationToken))
            return NotFound();

        string? dir = _configuration["Xpd:IdPhotosDirectory"];
        if (!IdPhotoResolver.TryResolvePath(dir, customerKey, out string path, out string contentType))
            return NotFound();

        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    private async Task<bool> CanAccessCustomerKeyForIdPhotoAsync(int customerKey, CancellationToken cancellationToken)
    {
        bool isHqUser = User.IsHqUser();
        int? userStoreId = User.GetStoreId();
        if (!isHqUser && userStoreId is null)
            return false;

        if (!isHqUser)
        {
            return await _db.Customers.AsNoTracking()
                .AnyAsync(
                    c => c.CustomerKey == customerKey && c.StoreId == userStoreId!.Value,
                    cancellationToken);
        }

        return await XpdCustomerKeyExistsAsync(customerKey, cancellationToken);
    }

    private async Task<bool> XpdCustomerKeyExistsAsync(int customerKey, CancellationToken cancellationToken)
    {
        DbConnection connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Customers WHERE CustomerKey = @k LIMIT 1";
        DbParameter p = cmd.CreateParameter();
        p.ParameterName = "@k";
        p.Value = customerKey;
        cmd.Parameters.Add(p);

        object? result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null && result != DBNull.Value;
    }

    private bool IdPhotoAvailableForProfile(XpdCustomerProfile p) =>
        !string.IsNullOrWhiteSpace(p.IdNo) &&
        IdPhotoResolver.TryResolvePath(_configuration["Xpd:IdPhotosDirectory"], p.CustomerKey, out _, out _);
}
