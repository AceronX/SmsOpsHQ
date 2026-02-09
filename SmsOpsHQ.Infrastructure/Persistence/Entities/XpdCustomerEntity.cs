namespace SmsOpsHQ.Infrastructure.Persistence.Entities;

// EF Core entity mapping to the XPD_Customers mirror table.
// Synced from XPawn MS Access database via VBScript streaming.
public sealed class XpdCustomerEntity
{
    public int Key { get; set; }
    public string? LastName { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? ResPhone { get; set; }
    public string? BusPhone { get; set; }
    public string? Email { get; set; }
    public string? DOB { get; set; }
    public string? SSN { get; set; }
    public string? IDNo { get; set; }
    public string? IDIssueState { get; set; }
    public string? Notes { get; set; }
    public string? FirstTransaction { get; set; }
    public string? LastTransaction { get; set; }
    public string? Warning { get; set; }
    public string? SyncedAt { get; set; }
}
