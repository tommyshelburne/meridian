using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Crm;

/// <summary>
/// No-op CRM adapter that lets the pipeline orchestrate "create deal" without an
/// external system. Logs what would have happened and returns synthetic IDs so
/// downstream audit / enrollment flows still see a valid pipeline run.
///
/// Per-tenant CRM provider routing (Pipedrive, HubSpot, Salesforce) is a v3.1
/// concern; this stub keeps v3.0 deployable without forcing every tenant to
/// configure a CRM up front.
/// </summary>
public class NoopCrmClient : ICrmClient
{
    private readonly ILogger<NoopCrmClient> _logger;

    public NoopCrmClient(ILogger<NoopCrmClient> logger) => _logger = logger;

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
