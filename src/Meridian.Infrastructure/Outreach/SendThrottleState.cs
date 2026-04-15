namespace Meridian.Infrastructure.Outreach;

public class SendThrottleState
{
    private int _sentToday;
    private DateOnly _currentDay;
    private readonly object _lock = new();

    public int DailyCap { get; set; } = 50;

    public bool IsCapReached
    {
        get
        {
            lock (_lock)
            {
                ResetIfNewDay();
                return _sentToday >= DailyCap;
            }
        }
    }

    public int SentToday
    {
        get
        {
            lock (_lock)
            {
                ResetIfNewDay();
                return _sentToday;
            }
        }
    }

    public void RecordSend()
    {
        lock (_lock)
        {
            ResetIfNewDay();
            _sentToday++;
        }
    }

    private void ResetIfNewDay()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_currentDay != today)
        {
            _currentDay = today;
            _sentToday = 0;
        }
    }
}
