namespace Meridian.Infrastructure.Outreach;

// Per-tenant counter so a high-volume tenant can't exhaust a global cap and
// starve quieter tenants. Each tenant gets its own daily counter that resets
// at UTC midnight on first read after the rollover.
public class SendThrottleState
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, TenantCounter> _counters = new();

    public int GetSentToday(Guid tenantId)
    {
        lock (_lock)
        {
            ResetIfNewDay(tenantId);
            return _counters.TryGetValue(tenantId, out var c) ? c.SentToday : 0;
        }
    }

    public void RecordSend(Guid tenantId)
    {
        lock (_lock)
        {
            ResetIfNewDay(tenantId);
            if (!_counters.TryGetValue(tenantId, out var c))
            {
                c = new TenantCounter { Day = Today() };
                _counters[tenantId] = c;
            }
            c.SentToday++;
        }
    }

    private void ResetIfNewDay(Guid tenantId)
    {
        if (_counters.TryGetValue(tenantId, out var c) && c.Day != Today())
        {
            c.SentToday = 0;
            c.Day = Today();
        }
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private class TenantCounter
    {
        public int SentToday;
        public DateOnly Day;
    }
}
