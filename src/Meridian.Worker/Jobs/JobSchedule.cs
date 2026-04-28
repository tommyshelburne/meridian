namespace Meridian.Worker.Jobs;

public record JobSchedule(string JobName, TimeSpan Interval, TimeSpan? RunAtUtc = null)
{
    public static readonly JobSchedule[] Default =
    [
        new("Ingestion", TimeSpan.FromHours(24), TimeSpan.FromHours(6)),      // Daily at 06:00 UTC
        new("Processing", TimeSpan.FromMinutes(30)),                          // Every 30 min — picks up newly-ingested opps
        new("BidMonitor", TimeSpan.FromHours(24), TimeSpan.FromHours(12)),    // Daily at 12:00 UTC
        new("Sequence", TimeSpan.FromHours(2)),                               // Every 2 hours
        new("CrmTokenRefresh", TimeSpan.FromMinutes(15)),                     // Every 15 minutes
    ];
}
