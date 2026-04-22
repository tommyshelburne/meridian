namespace Meridian.Infrastructure.Scoring;

public class ScoringConfiguration
{
    public const string SectionName = "Scoring";

    public IReadOnlyCollection<string> LaneKeywords { get; init; } = new[]
    {
        "contact center",
        "call center",
        "ivr",
        "interactive voice response",
        "citizen services",
        "constituent services",
        "help desk",
        "helpdesk",
        "customer service",
        "self-service",
        "voice bot",
        "chatbot",
        "virtual agent"
    };

    public IReadOnlyCollection<string> WinThemeKeywords { get; init; } = new[]
    {
        "modernization",
        "modernize",
        "ai-powered",
        "artificial intelligence",
        "automation",
        "cloud migration",
        "digital transformation",
        "self-service",
        "natural language",
        "transformation"
    };

    public IReadOnlyCollection<string> LegacyIncumbentKeywords { get; init; } = new[]
    {
        "nuance",
        "avaya",
        "cisco ucce",
        "genesys engage",
        "interactive intelligence",
        "verint",
        "edify"
    };

    public IReadOnlyCollection<string> RecompeteKeywords { get; init; } = new[]
    {
        "recompete",
        "re-compete",
        "follow-on",
        "follow on contract",
        "successor contract",
        "renewal of",
        "replacement of",
        "incumbent contract"
    };

    public IReadOnlyCollection<string> PastPerformanceNaicsCodes { get; init; } = new[]
    {
        "561422", // Telemarketing Bureaus and Other Contact Centers
        "541512", // Computer Systems Design Services
        "541519", // Other Computer Related Services
        "541611", // Administrative Management and General Management Consulting Services
        "561110", // Office Administrative Services
        "561499", // All Other Business Support Services
        "518210", // Data Processing, Hosting, and Related Services
        "541330"  // Engineering Services
    };

    public IReadOnlyCollection<string> Tier1States { get; init; } = new[]
    {
        "CA", "TX", "FL", "VA", "MD", "GA", "NY", "IL", "WA", "CO", "UT"
    };

    public decimal PerSeatAnnualPrice { get; init; } = 199m * 12m;
    public int FederalSeatProxy { get; init; } = 200;
    public decimal FederalSeatProxyMinValue { get; init; } = 5_000_000m;

    public int PursueThreshold { get; init; } = 10;
    public int PartnerThreshold { get; init; } = 6;
}
