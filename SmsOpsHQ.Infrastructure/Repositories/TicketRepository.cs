using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;

namespace SmsOpsHQ.Infrastructure.Repositories;

/// <summary>
/// Reads the XPD-synced Tickets table via raw SQL on the shared
/// <see cref="AppDbContext"/> connection (no EF entity mapping for Tickets).
/// </summary>
/// <remarks>
/// Before XPD sync runs at least once on a fresh store DB, the Tickets table
/// can be missing. That specific failure mode is treated as "no rows" with a
/// single warning log; any other database error propagates so the caller and
/// monitoring can see it.
/// </remarks>
public sealed class TicketRepository : ITicketRepository
{
    private const string TicketColumns =
        "Key, CustomerKey, TransNo, Type, Active, Amount, CurrentBalance, " +
        "IssueDate, DueDate, DateClosed, HowClosed, Status, Notes, Item, " +
        "OperatorInitials, GunTicket, LostTicket, PaidTillDate, LastDate, " +
        "ChargesDue, StandardCharges, StandardPU, FullTermPU, FulltermRenew";

    // SQLite reports a missing table as "SQLite Error 1: 'no such table: ...'".
    // We match the textual marker so Infrastructure doesn't need a direct
    // Microsoft.Data.Sqlite package reference -- it's wired transitively today
    // via EF Core and we'd like to keep it that way.
    private const string MissingTableMarker = "no such table";

    private readonly AppDbContext _db;
    private readonly ILogger<TicketRepository> _logger;

    public TicketRepository(AppDbContext db, ILogger<TicketRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<List<Ticket>> GetByCustomerKeyAsync(
        int customerKey,
        CancellationToken cancellationToken = default) =>
        GetByCustomerKeysAsync(new List<int> { customerKey }, cancellationToken);

    public async Task<List<Ticket>> GetByCustomerKeysAsync(
        List<int> customerKeys,
        CancellationToken cancellationToken = default)
    {
        if (customerKeys.Count == 0)
            return new List<Ticket>();

        string placeholders = string.Join(",", customerKeys.Select((_, i) => $"@k{i}"));
        string sql =
            $"SELECT {TicketColumns} " +
            $"FROM Tickets " +
            $"WHERE CustomerKey IN ({placeholders}) " +
            $"ORDER BY IssueDate DESC";

        return await ExecuteReaderAsync(
            sql,
            cmd =>
            {
                for (int i = 0; i < customerKeys.Count; i++)
                {
                    DbParameter p = cmd.CreateParameter();
                    p.ParameterName = $"@k{i}";
                    p.Value = customerKeys[i];
                    cmd.Parameters.Add(p);
                }
            },
            MapTicketFromReader,
            cancellationToken);
    }

    public async Task<Ticket?> GetByKeyAsync(
        int ticketKey,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            $"SELECT {TicketColumns} " +
            $"FROM Tickets " +
            $"WHERE Key = @key";

        List<Ticket> rows = await ExecuteReaderAsync(
            sql,
            cmd =>
            {
                DbParameter p = cmd.CreateParameter();
                p.ParameterName = "@key";
                p.Value = ticketKey;
                cmd.Parameters.Add(p);
            },
            MapTicketFromReader,
            cancellationToken,
            limit: 1);

        return rows.Count == 0 ? null : rows[0];
    }

    // Shared connection + reader plumbing. Centralizes the "open connection,
    // create command, bind parameters, iterate reader, map rows" boilerplate
    // and the missing-table catch in one place.
    private async Task<List<Ticket>> ExecuteReaderAsync(
        string sql,
        Action<DbCommand> bindParameters,
        Func<DbDataReader, Ticket> map,
        CancellationToken ct,
        int? limit = null)
    {
        DbConnection conn = _db.Database.GetDbConnection();
        try
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            using DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            bindParameters(cmd);

            List<Ticket> results = new();
            using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(map(reader));
                if (limit is int max && results.Count >= max)
                    break;
            }
            return results;
        }
        catch (DbException ex) when (IsMissingTicketsTable(ex))
        {
            _logger.LogWarning(
                "Tickets table is missing in the local database; returning no rows. " +
                "This is expected before XPD sync first runs. ({Error})",
                ex.Message);
            return new List<Ticket>();
        }
    }

    private static bool IsMissingTicketsTable(DbException ex) =>
        ex.Message?.IndexOf(MissingTableMarker, StringComparison.OrdinalIgnoreCase) >= 0;

    private static Ticket MapTicketFromReader(DbDataReader r) => new()
    {
        Key = r.GetInt32(r.GetOrdinal("Key")),
        CustomerKey = r.GetInt32(r.GetOrdinal("CustomerKey")),
        TransNo = NullableInt(r, "TransNo"),
        Type = NullableInt(r, "Type"),
        Active = NullableInt(r, "Active"),
        Amount = NullableDouble(r, "Amount"),
        CurrentBalance = NullableDouble(r, "CurrentBalance"),
        IssueDate = NullableString(r, "IssueDate"),
        DueDate = NullableString(r, "DueDate"),
        DateClosed = NullableString(r, "DateClosed"),
        HowClosed = NullableString(r, "HowClosed"),
        Status = NullableString(r, "Status"),
        Notes = NullableString(r, "Notes"),
        Item = NullableString(r, "Item"),
        OperatorInitials = NullableString(r, "OperatorInitials"),
        GunTicket = NullableInt(r, "GunTicket"),
        LostTicket = NullableInt(r, "LostTicket"),
        PaidTillDate = NullableString(r, "PaidTillDate"),
        LastDate = NullableString(r, "LastDate"),
        ChargesDue = NullableDouble(r, "ChargesDue"),
        StandardCharges = NullableDouble(r, "StandardCharges"),
        StandardPU = NullableDouble(r, "StandardPU"),
        FullTermPU = NullableDouble(r, "FullTermPU"),
        FulltermRenew = NullableDouble(r, "FulltermRenew"),
    };

    private static int? NullableInt(DbDataReader r, string column)
    {
        int ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetInt32(ordinal);
    }

    private static double? NullableDouble(DbDataReader r, string column)
    {
        int ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetDouble(ordinal);
    }

    private static string? NullableString(DbDataReader r, string column)
    {
        int ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }
}
