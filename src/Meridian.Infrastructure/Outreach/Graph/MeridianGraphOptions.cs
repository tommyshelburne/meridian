namespace Meridian.Infrastructure.Outreach.Graph;

public class MeridianGraphOptions
{
    public const string SectionName = "MeridianGraph";

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Mailbox { get; set; } = "outreach@kombea.com";
    public string LoginBaseUrl { get; set; } = "https://login.microsoftonline.com";
    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
    public int MessagePageSize { get; set; } = 100;
    public int TokenExpirySafetySeconds { get; set; } = 300;
}
