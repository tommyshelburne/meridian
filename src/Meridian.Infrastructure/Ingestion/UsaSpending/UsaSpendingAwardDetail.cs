using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Ingestion.UsaSpending;

public class UsaSpendingAwardDetail
{
    [JsonPropertyName("recipient")]
    public UsaSpendingAwardRecipient? Recipient { get; set; }
}

public class UsaSpendingAwardRecipient
{
    [JsonPropertyName("recipient_name")]
    public string? RecipientName { get; set; }

    [JsonPropertyName("business_categories")]
    public List<string>? BusinessCategories { get; set; }

    [JsonPropertyName("recipient_primary_business_representative")]
    public UsaSpendingBusinessRepresentative? PrimaryBusinessRepresentative { get; set; }
}

public class UsaSpendingBusinessRepresentative
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
}
