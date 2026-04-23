namespace Meridian.Infrastructure.Outreach;

public class EmailComplianceOptions
{
    public const string SectionName = "EmailCompliance";

    public string PhysicalAddress { get; set; } = string.Empty;
    public string UnsubscribeBaseUrl { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
}
