namespace Meridian.Worker.Jobs;

public interface IMeridianJob
{
    string Name { get; }
    Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken ct);
}
