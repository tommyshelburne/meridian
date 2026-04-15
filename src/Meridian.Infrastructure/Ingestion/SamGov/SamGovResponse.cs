using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Ingestion.SamGov;

public class SamGovSearchResponse
{
    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("opportunitiesData")]
    public List<SamGovOpportunity> OpportunitiesData { get; set; } = new();
}

public class SamGovOpportunity
{
    [JsonPropertyName("noticeId")]
    public string NoticeId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("subtier")]
    public string? SubTier { get; set; }

    [JsonPropertyName("office")]
    public string? Office { get; set; }

    [JsonPropertyName("postedDate")]
    public string? PostedDate { get; set; }

    [JsonPropertyName("responseDeadLine")]
    public string? ResponseDeadline { get; set; }

    [JsonPropertyName("naicsCode")]
    public string? NaicsCode { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("baseType")]
    public string? BaseType { get; set; }

    [JsonPropertyName("archiveType")]
    public string? ArchiveType { get; set; }

    [JsonPropertyName("typeOfSetAside")]
    public string? TypeOfSetAside { get; set; }

    [JsonPropertyName("award")]
    public SamGovAward? Award { get; set; }

    [JsonPropertyName("pointOfContact")]
    public List<SamGovPointOfContact>? PointOfContact { get; set; }

    [JsonPropertyName("resourceLinks")]
    public List<string>? ResourceLinks { get; set; }
}

public class SamGovAward
{
    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("awardee")]
    public SamGovAwardee? Awardee { get; set; }
}

public class SamGovAwardee
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class SamGovPointOfContact
{
    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
