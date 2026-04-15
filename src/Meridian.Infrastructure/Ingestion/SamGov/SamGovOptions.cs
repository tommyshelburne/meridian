namespace Meridian.Infrastructure.Ingestion.SamGov;

public class SamGovOptions
{
    public const string SectionName = "SamGov";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.sam.gov/opportunities/v2/search";
    public IReadOnlyList<string> Keywords { get; set; } = new[]
    {
        "contact center", "call center", "IVR", "citizen services",
        "customer service", "helpdesk"
    };
    public int PageSize { get; set; } = 25;
    public int MaxPages { get; set; } = 10;
    public int LookbackDays { get; set; } = 7;
}
