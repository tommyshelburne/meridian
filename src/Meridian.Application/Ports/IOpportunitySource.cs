using Meridian.Application.Common;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Ports;

public interface IOpportunitySource
{
    string SourceName { get; }
    Task<ServiceResult<IReadOnlyList<Opportunity>>> FetchOpportunitiesAsync(Guid tenantId, CancellationToken ct);
}
