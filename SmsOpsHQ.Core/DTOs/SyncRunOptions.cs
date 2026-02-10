namespace SmsOpsHQ.Core.DTOs;

public sealed class SyncRunOptions
{
    public string? XpdPath { get; set; }
    public string? MdwPath { get; set; }
    public string? XpdUser { get; set; }
    public string? XpdPassword { get; set; }
}
