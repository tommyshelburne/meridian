using Meridian.Application.Common;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Ports;

public interface ICrmClient
{
    Task<ServiceResult<string>> FindOrCreateOrganizationAsync(string agencyName, CancellationToken ct);
    Task<ServiceResult<string>> CreateDealAsync(Opportunity opportunity, string organizationId, CancellationToken ct);
    Task<ServiceResult> UpdateDealStageAsync(string dealId, string stage, CancellationToken ct);
    Task<ServiceResult> AddActivityAsync(string dealId, string type, string description, CancellationToken ct);
}
