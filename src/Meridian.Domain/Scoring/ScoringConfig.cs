namespace Meridian.Domain.Scoring;

public class ScoringConfig
{
    public IReadOnlyList<string> TitleKeywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DescriptionKeywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WinThemeKeywords { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> KnownCompetitors { get; init; } = Array.Empty<string>();

    public static ScoringConfig KomBeaDefault => new()
    {
        TitleKeywords = new[]
        {
            "contact center", "call center", "IVR", "citizen services",
            "customer service", "helpdesk", "help desk"
        },
        DescriptionKeywords = new[]
        {
            "contact center", "call center", "IVR", "citizen services",
            "customer service", "helpdesk", "help desk", "interactive voice",
            "customer experience", "CX platform", "omnichannel"
        },
        WinThemeKeywords = new[]
        {
            "cloud migration", "modernization", "digital transformation",
            "AI", "workforce optimization", "quality assurance", "analytics"
        },
        KnownCompetitors = new[]
        {
            "Nuance", "Maximus", "NICE", "Genesys", "Avaya",
            "ATI Government Solutions", "Accenture Federal",
            "Leidos", "SAIC", "Booz Allen", "Peraton", "KECH", "TAURUS"
        }
    };
}
