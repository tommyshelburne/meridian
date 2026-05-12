namespace Meridian.Infrastructure.Outreach.Postmark;

public class PostmarkInboundOptions
{
    public const string SectionName = "PostmarkInbound";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
