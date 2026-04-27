using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Ports;

public interface ICrmAdapter
{
    CrmProvider Provider { get; }

    Task<ServiceResult<string>> FindOrCreateOrganizationAsync(
        CrmConnectionContext ctx, string agencyName, CancellationToken ct);

    Task<ServiceResult<string>> CreateDealAsync(
        CrmConnectionContext ctx, Opportunity opportunity, string organizationId, CancellationToken ct);

    Task<ServiceResult> UpdateDealStageAsync(
        CrmConnectionContext ctx, string dealId, string stage, CancellationToken ct);

    Task<ServiceResult> AddActivityAsync(
        CrmConnectionContext ctx, string dealId, string type, string description, CancellationToken ct);
}
