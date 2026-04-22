namespace Meridian.Infrastructure.Outreach;

public class SendThrottleOptions
{
    public const string SectionName = "SendThrottle";

    public int DailyCap { get; set; } = 50;
    public TimeSpan SendWindowStart { get; set; } = TimeSpan.FromHours(13); // 13:00 UTC ~= 09:00 ET
    public TimeSpan SendWindowEnd { get; set; } = TimeSpan.FromHours(22);   // 22:00 UTC ~= 18:00 ET
    public bool EnforceSendWindow { get; set; } = true;
}
