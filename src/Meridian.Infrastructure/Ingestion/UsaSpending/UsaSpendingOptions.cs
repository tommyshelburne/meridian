namespace Meridian.Infrastructure.Ingestion.UsaSpending;

public class UsaSpendingOptions
{
    public const string SectionName = "UsaSpending";

    public string BaseUrl { get; set; } = "https://api.usaspending.gov/api/v2/search/spending_by_award/";
    public string AwardDetailBaseUrl { get; set; } = "https://api.usaspending.gov/api/v2/awards/";
    public int PageSize { get; set; } = 50;
    public int MaxPages { get; set; } = 5;
    public int DefaultLookbackDays { get; set; } = 30;
    public int PocEnricherMaxAwards { get; set; } = 5;
    public int PocEnricherLookbackDays { get; set; } = 730; // 2 years
    public float PocEnricherBaseConfidence { get; set; } = 0.65f;
}
