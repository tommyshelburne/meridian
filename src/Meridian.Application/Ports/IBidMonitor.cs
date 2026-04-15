using Meridian.Application.Common;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Ports;

public record AmendmentUpdate(string ExternalId, DateTimeOffset AmendedAt, string? NewTitle, DateTimeOffset? NewDeadline);

public interface IBidMonitor
{
    Task<ServiceResult<IReadOnlyList<AmendmentUpdate>>> CheckForUpdatesAsync(
        IReadOnlyList<Opportunity> watchedOpportunities, CancellationToken ct);
}
