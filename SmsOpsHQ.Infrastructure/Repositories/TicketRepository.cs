using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SmsOpsHQ.Core.Entities;
using SmsOpsHQ.Core.Repositories;
using SmsOpsHQ.Infrastructure.Persistence;

namespace SmsOpsHQ.Infrastructure.Repositories;

// EF Core + raw SQL implementation of ITicketRepository.
// Queries Tickets and Items tables using raw SQL via ADO.NET.
public sealed class TicketRepository : ITicketRepository
{
    private readonly AppDbContext _db;

    public TicketRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Ticket>> GetByCustomerKeyAsync(int customerKey,
        CancellationToken cancellationToken = default)
    {
        return await GetByCustomerKeysAsync(new List<int> { customerKey }, cancellationToken);
    }

    public async Task<List<Ticket>> GetByCustomerKeysAsync(List<int> customerKeys,
        CancellationToken cancellationToken = default)
    {
        if (customerKeys.Count == 0)
            return new List<Ticket>();

        List<Ticket> tickets = new();
        DbConnection connection = _db.Database.GetDbConnection();

        try
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            using DbCommand command = connection.CreateCommand();

            string placeholders = string.Join(",", customerKeys.Select((_, i) => $"@k{i}"));
            command.CommandText = $@"
                SELECT Key, CustomerKey, TransNo, Type, Active, Amount, CurrentBalance,
                       IssueDate, DueDate, DateClosed, HowClosed, Status, Notes, Item,
                       OperatorInitials, GunTicket, LostTicket, PaidTillDate, LastDate,
                       ChargesDue, StandardCharges, StandardPU, FullTermPU, FulltermRenew
                FROM Tickets
                WHERE CustomerKey IN ({placeholders})
                ORDER BY IssueDate DESC";

            for (int i = 0; i < customerKeys.Count; i++)
            {
                DbParameter param = command.CreateParameter();
                param.ParameterName = $"@k{i}";
                param.Value = customerKeys[i];
                command.Parameters.Add(param);
            }

            using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tickets.Add(MapTicketFromReader(reader));
            }
        }
        catch (Exception)
        {
            // Tickets table may not exist until migration runs.
            // Return empty list gracefully.
        }

        return tickets;
    }

    public async Task<Ticket?> GetByKeyAsync(int ticketKey,
        CancellationToken cancellationToken = default)
    {
        DbConnection connection = _db.Database.GetDbConnection();

        try
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            using DbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Key, CustomerKey, TransNo, Type, Active, Amount, CurrentBalance,
                       IssueDate, DueDate, DateClosed, HowClosed, Status, Notes, Item,
                       OperatorInitials, GunTicket, LostTicket, PaidTillDate, LastDate,
                       ChargesDue, StandardCharges, StandardPU, FullTermPU, FulltermRenew
                FROM Tickets
                WHERE Key = @key";

            DbParameter param = command.CreateParameter();
            param.ParameterName = "@key";
            param.Value = ticketKey;
            command.Parameters.Add(param);

            using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return MapTicketFromReader(reader);
            }
        }
        catch (Exception)
        {
            // Tickets table may not exist yet.
        }

        return null;
    }

    private static Ticket MapTicketFromReader(DbDataReader reader)
    {
        return new Ticket
        {
            Key = reader.GetInt32(reader.GetOrdinal("Key")),
            CustomerKey = reader.GetInt32(reader.GetOrdinal("CustomerKey")),
            TransNo = reader.IsDBNull(reader.GetOrdinal("TransNo")) ? null : reader.GetInt32(reader.GetOrdinal("TransNo")),
            Type = reader.IsDBNull(reader.GetOrdinal("Type")) ? null : reader.GetInt32(reader.GetOrdinal("Type")),
            Active = reader.IsDBNull(reader.GetOrdinal("Active")) ? null : reader.GetInt32(reader.GetOrdinal("Active")),
            Amount = reader.IsDBNull(reader.GetOrdinal("Amount")) ? null : reader.GetDouble(reader.GetOrdinal("Amount")),
            CurrentBalance = reader.IsDBNull(reader.GetOrdinal("CurrentBalance")) ? null : reader.GetDouble(reader.GetOrdinal("CurrentBalance")),
            IssueDate = reader.IsDBNull(reader.GetOrdinal("IssueDate")) ? null : reader.GetString(reader.GetOrdinal("IssueDate")),
            DueDate = reader.IsDBNull(reader.GetOrdinal("DueDate")) ? null : reader.GetString(reader.GetOrdinal("DueDate")),
            DateClosed = reader.IsDBNull(reader.GetOrdinal("DateClosed")) ? null : reader.GetString(reader.GetOrdinal("DateClosed")),
            HowClosed = reader.IsDBNull(reader.GetOrdinal("HowClosed")) ? null : reader.GetString(reader.GetOrdinal("HowClosed")),
            Status = reader.IsDBNull(reader.GetOrdinal("Status")) ? null : reader.GetString(reader.GetOrdinal("Status")),
            Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
            Item = reader.IsDBNull(reader.GetOrdinal("Item")) ? null : reader.GetString(reader.GetOrdinal("Item")),
            OperatorInitials = reader.IsDBNull(reader.GetOrdinal("OperatorInitials")) ? null : reader.GetString(reader.GetOrdinal("OperatorInitials")),
            GunTicket = reader.IsDBNull(reader.GetOrdinal("GunTicket")) ? null : reader.GetInt32(reader.GetOrdinal("GunTicket")),
            LostTicket = reader.IsDBNull(reader.GetOrdinal("LostTicket")) ? null : reader.GetInt32(reader.GetOrdinal("LostTicket")),
            PaidTillDate = reader.IsDBNull(reader.GetOrdinal("PaidTillDate")) ? null : reader.GetString(reader.GetOrdinal("PaidTillDate")),
            LastDate = reader.IsDBNull(reader.GetOrdinal("LastDate")) ? null : reader.GetString(reader.GetOrdinal("LastDate")),
            ChargesDue = reader.IsDBNull(reader.GetOrdinal("ChargesDue")) ? null : reader.GetDouble(reader.GetOrdinal("ChargesDue")),
            StandardCharges = reader.IsDBNull(reader.GetOrdinal("StandardCharges")) ? null : reader.GetDouble(reader.GetOrdinal("StandardCharges")),
            StandardPU = reader.IsDBNull(reader.GetOrdinal("StandardPU")) ? null : reader.GetDouble(reader.GetOrdinal("StandardPU")),
            FullTermPU = reader.IsDBNull(reader.GetOrdinal("FullTermPU")) ? null : reader.GetDouble(reader.GetOrdinal("FullTermPU")),
            FulltermRenew = reader.IsDBNull(reader.GetOrdinal("FulltermRenew")) ? null : reader.GetDouble(reader.GetOrdinal("FulltermRenew"))
        };
    }
}
