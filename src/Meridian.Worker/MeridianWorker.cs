using Meridian.Worker.Jobs;

namespace Meridian.Worker;

public class MeridianWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyDictionary<string, IMeridianJob> _jobs;
    private readonly ILogger<MeridianWorker> _logger;

    public MeridianWorker(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IMeridianJob> jobs,
        ILogger<MeridianWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _jobs = jobs.ToDictionary(j => j.Name);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Meridian Worker starting with {Count} jobs registered", _jobs.Count);

        var tasks = JobSchedule.Default.Select(schedule => RunJobLoopAsync(schedule, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task RunJobLoopAsync(JobSchedule schedule, CancellationToken ct)
    {
        if (!_jobs.TryGetValue(schedule.JobName, out var job))
        {
            _logger.LogWarning("No job registered for schedule {JobName}", schedule.JobName);
            return;
        }

        // If RunAtUtc is set, wait until that time today (or tomorrow if already past)
        if (schedule.RunAtUtc.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            var todayRun = now.Date + schedule.RunAtUtc.Value;
            var nextRun = todayRun > now.DateTime ? todayRun : todayRun.AddDays(1);
            var delay = nextRun - now.DateTime;
            _logger.LogInformation("Job {JobName} scheduled for {NextRun} UTC (in {Delay})",
                schedule.JobName, nextRun, delay);
            await Task.Delay(delay, ct);
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting job {JobName}", schedule.JobName);
                using var scope = _scopeFactory.CreateScope();
                await job.ExecuteAsync(scope.ServiceProvider, ct);
                _logger.LogInformation("Job {JobName} completed", schedule.JobName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Job {JobName} failed", schedule.JobName);
            }

            await Task.Delay(schedule.Interval, ct);
        }
    }
}
