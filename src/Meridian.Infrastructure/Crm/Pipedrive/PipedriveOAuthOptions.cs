namespace Meridian.Infrastructure.Crm.Pipedrive;

public class PipedriveOAuthOptions
{
    public const string SectionName = "Pipedrive:OAuth";

    public string AuthorizeUrl { get; set; } = "https://oauth.pipedrive.com/oauth/authorize";
    public string TokenUrl { get; set; } = "https://oauth.pipedrive.com/oauth/token";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}
