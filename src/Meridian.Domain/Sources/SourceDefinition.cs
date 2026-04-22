namespace Meridian.Domain.Sources;

public class SourceDefinition
{
    public const int AutoDisableAfterConsecutiveFailures = 5;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public SourceAdapterType AdapterType { get; private set; }
    public string Name { get; private set; } = null!;
    public string ParametersJson { get; private set; } = "{}";
    public string? Schedule { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }
    public SourceRunStatus LastRunStatus { get; private set; }
    public string? LastRunError { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SourceDefinition() { }

    public static SourceDefinition Create(
        Guid tenantId,
        SourceAdapterType adapterType,
        string name,
        string parametersJson,
        string? schedule = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Source name is required.", nameof(name));

        var now = DateTimeOffset.UtcNow;
        return new SourceDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AdapterType = adapterType,
            Name = name.Trim(),
            ParametersJson = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson,
            Schedule = schedule,
            IsEnabled = true,
            LastRunStatus = SourceRunStatus.NeverRun,
            ConsecutiveFailures = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateParameters(string parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            throw new ArgumentException("Parameters are required.", nameof(parametersJson));
        ParametersJson = parametersJson;
        Touch();
    }

    public void UpdateSchedule(string? schedule)
    {
        Schedule = schedule;
        Touch();
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Source name is required.", nameof(name));
        Name = name.Trim();
        Touch();
    }

    public void Enable()
    {
        if (IsEnabled) return;
        IsEnabled = true;
        if (LastRunStatus == SourceRunStatus.Disabled)
            LastRunStatus = SourceRunStatus.NeverRun;
        ConsecutiveFailures = 0;
        Touch();
    }

    public void Disable()
    {
        IsEnabled = false;
        LastRunStatus = SourceRunStatus.Disabled;
        Touch();
    }

    public void MarkRunStarted()
    {
        LastRunStatus = SourceRunStatus.Running;
        LastRunAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkRunSucceeded()
    {
        LastRunStatus = SourceRunStatus.Succeeded;
        LastRunAt = DateTimeOffset.UtcNow;
        LastRunError = null;
        ConsecutiveFailures = 0;
        Touch();
    }

    public void MarkRunFailed(string error)
    {
        LastRunStatus = SourceRunStatus.Failed;
        LastRunAt = DateTimeOffset.UtcNow;
        LastRunError = error;
        ConsecutiveFailures++;
        if (ConsecutiveFailures >= AutoDisableAfterConsecutiveFailures)
            Disable();
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
