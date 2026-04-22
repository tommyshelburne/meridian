namespace Meridian.Infrastructure.Outreach;

public class EmailComplianceOptions
{
    public const string SectionName = "EmailCompliance";

    public string PhysicalAddress { get; set; } = "KomBea, Inc., 222 S Main St, Suite 500, Salt Lake City, UT 84101";
    public string UnsubscribeBaseUrl { get; set; } = "https://meridian.kombea.com/unsubscribe";
    public string FromName { get; set; } = "KomBea";
    public string FromAddress { get; set; } = "outreach@kombea.com";
}
