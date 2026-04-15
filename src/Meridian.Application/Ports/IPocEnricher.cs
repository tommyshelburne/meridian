using Meridian.Application.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Ports;

public interface IPocEnricher
{
    string SourceName { get; }
    Task<ServiceResult<IReadOnlyList<Contact>>> EnrichAsync(Opportunity opportunity, Guid tenantId, CancellationToken ct);
}
