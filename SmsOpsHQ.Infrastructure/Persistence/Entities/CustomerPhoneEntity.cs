using System.ComponentModel.DataAnnotations.Schema;

namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity for the CustomerPhones table (phone index).
// SourceField is stored in legacy column "PhoneType" (ResPhone, BusPhone, Notes, TicketNotes).
public sealed class CustomerPhoneEntity
{
    public int CustomerKey { get; set; }
    public string PhoneNormalized { get; set; } = string.Empty;
    public string? PhoneOriginal { get; set; }

    [Column("PhoneType")]
    public string SourceField { get; set; } = string.Empty;

    public string? MatchType { get; set; }
    public bool IsDirect { get; set; }
}
