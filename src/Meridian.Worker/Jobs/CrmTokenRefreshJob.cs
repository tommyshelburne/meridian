using Meridian.Application.Crm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Worker.Jobs;

public class CrmTokenRefreshJob : IMeridianJob
{
    // Refresh anything expiring inside the next 30 minutes. Combined with the
    // 15-minute job interval this gives ~15 minutes of headroom over a worst-
    // case skipped run before lazy refresh-on-401 has to absorb the miss.
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public string Name => "CrmTokenRefresh";

    public async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken ct)
    {
        var logger = scopedProvider.GetRequiredService<ILogger<CrmTokenRefreshJob>>();
        var service = scopedProvider.GetRequiredService<CrmConnectionService>();

        var result = await service.RefreshExpiringAsync(Window, ct);

        if (result.Candidates == 0)
            logger.LogDebug("CRM token refresh sweep: no connections within the {Window} window", Window);
        else
            logger.LogInformation(
                "CRM token refresh sweep: {Refreshed} refreshed / {Failed} failed of {Candidates} candidates",
                result.Refreshed, result.Failed, result.Candidates);
    }
}
