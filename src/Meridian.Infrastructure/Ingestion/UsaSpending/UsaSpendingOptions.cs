namespace Meridian.Infrastructure.Ingestion.UsaSpending;

public class UsaSpendingOptions
{
    public const string SectionName = "UsaSpending";

    public string BaseUrl { get; set; } = "https://api.usaspending.gov/api/v2/search/spending_by_award/";
    public int PageSize { get; set; } = 50;
    public int MaxPages { get; set; } = 5;
    public int DefaultLookbackDays { get; set; } = 30;
}
