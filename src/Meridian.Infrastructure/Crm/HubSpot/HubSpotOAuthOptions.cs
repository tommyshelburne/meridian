namespace Meridian.Infrastructure.Crm.HubSpot;

public class HubSpotOAuthOptions
{
    public const string SectionName = "HubSpot:OAuth";

    public string AuthorizeUrl { get; set; } = "https://app.hubspot.com/oauth/authorize";
    public string TokenUrl { get; set; } = "https://api.hubapi.com/oauth/v1/token";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "crm.objects.companies.write crm.objects.deals.write";
}
