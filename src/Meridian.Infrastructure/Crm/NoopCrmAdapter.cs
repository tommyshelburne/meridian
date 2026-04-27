using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Crm;

// Default adapter for tenants without a configured CRM connection. Logs each
// would-be call and returns synthetic IDs so downstream audit / enrollment
// still see a coherent pipeline run.
public class NoopCrmAdapter : ICrmAdapter
{
    private readonly ILogger<NoopCrmAdapter> _logger;

    public NoopCrmAdapter(ILogger<NoopCrmAdapter> logger) => _logger = logger;

    public CrmProvider Provider => CrmProvider.None;

    public Task<ServiceResult<string>> FindOrCreateOrganizationAsync(string agencyName, CancellationToken ct)
    {
        var id = $"noop-org:{agencyName.ToLowerInvariant().Replace(' ', '-')}";
        _logger.LogInformation("CRM: would find-or-create organization {AgencyName} -> {OrgId}", agencyName, id);
        return Task.FromResult(ServiceResult<string>.Ok(id));
    }

    public Task<ServiceResult<string>> CreateDealAsync(Opportunity opportunity, string organizationId, CancellationToken ct)
    {
        var id = $"noop-deal:{opportunity.Id:N}";
        _logger.LogInformation("CRM: would create deal for {Title} under {OrgId} -> {DealId}",
            opportunity.Title, organizationId, id);
        return Task.FromResult(ServiceResult<string>.Ok(id));
    }

    public Task<ServiceResult> UpdateDealStageAsync(string dealId, string stage, CancellationToken ct)
    {
        _logger.LogInformation("CRM: would move deal {DealId} -> stage {Stage}", dealId, stage);
        return Task.FromResult(ServiceResult.Ok());
    }

    public Task<ServiceResult> AddActivityAsync(string dealId, string type, string description, CancellationToken ct)
    {
        _logger.LogInformation("CRM: would log activity on {DealId}: {Type} — {Description}", dealId, type, description);
        return Task.FromResult(ServiceResult.Ok());
    }
}
