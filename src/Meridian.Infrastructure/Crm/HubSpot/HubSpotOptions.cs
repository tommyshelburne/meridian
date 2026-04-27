namespace Meridian.Infrastructure.Crm.HubSpot;

public class HubSpotOptions
{
    public const string SectionName = "HubSpot";

    public string BaseUrl { get; set; } = "https://api.hubapi.com/";
}
