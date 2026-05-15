namespace SmsOpsHQ.Core.Models;

// One row from CustomerPhones for a normalized phone lookup, with ranking for identity resolution.
public sealed class CustomerPhoneMatch
{
    public int CustomerKey { get; set; }
    public string PhoneNormalized { get; set; } = "";
    public string SourceField { get; set; } = "";
    public string MatchType { get; set; } = "";
    public bool IsDirect { get; set; }
    public int MatchRank { get; set; }
}
