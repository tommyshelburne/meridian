using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Ingestion.UsaSpending;

public class UsaSpendingSearchRequest
{
    [JsonPropertyName("filters")]
    public UsaSpendingFilters Filters { get; set; } = new();

    [JsonPropertyName("fields")]
    public List<string> Fields { get; set; } = new()
    {
        "Award ID", "Recipient Name", "Award Amount", "Description",
        "Awarding Agency", "Awarding Sub Agency", "NAICS Code",
        "Start Date", "End Date", "Last Modified Date"
    };

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 50;

    [JsonPropertyName("sort")]
    public string Sort { get; set; } = "Last Modified Date";

    [JsonPropertyName("order")]
    public string Order { get; set; } = "desc";
}

public class UsaSpendingFilters
{
    [JsonPropertyName("award_type_codes")]
    public List<string> AwardTypeCodes { get; set; } = new() { "A", "B", "C", "D" };

    [JsonPropertyName("time_period")]
    public List<UsaSpendingTimePeriod> TimePeriod { get; set; } = new();

    [JsonPropertyName("naics_codes")]
    public List<string>? NaicsCodes { get; set; }

    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }

    [JsonPropertyName("award_amounts")]
    public List<UsaSpendingAmountRange>? AwardAmounts { get; set; }
}

public class UsaSpendingTimePeriod
{
    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("end_date")]
    public string EndDate { get; set; } = string.Empty;
}

public class UsaSpendingAmountRange
{
    [JsonPropertyName("lower_bound")]
    public decimal? LowerBound { get; set; }
}

public class UsaSpendingSearchResponse
{
    [JsonPropertyName("results")]
    public List<UsaSpendingResult> Results { get; set; } = new();

    [JsonPropertyName("page_metadata")]
    public UsaSpendingPageMetadata? PageMetadata { get; set; }
}

public class UsaSpendingResult
{
    [JsonPropertyName("Award ID")]
    public string? AwardId { get; set; }

    [JsonPropertyName("Recipient Name")]
    public string? RecipientName { get; set; }

    [JsonPropertyName("Award Amount")]
    public decimal? AwardAmount { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("Awarding Agency")]
    public string? AwardingAgency { get; set; }

    [JsonPropertyName("Awarding Sub Agency")]
    public string? AwardingSubAgency { get; set; }

    [JsonPropertyName("NAICS Code")]
    public string? NaicsCode { get; set; }

    [JsonPropertyName("Start Date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("End Date")]
    public string? EndDate { get; set; }

    [JsonPropertyName("Last Modified Date")]
    public string? LastModifiedDate { get; set; }
}

public class UsaSpendingPageMetadata
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("hasNext")]
    public bool HasNext { get; set; }
}
