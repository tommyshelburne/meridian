using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Sources;
using Meridian.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Ingestion;

public class IngestionOrchestrator
{
    private readonly ISourceDefinitionRepository _sources;
    private readonly ISourceAdapterFactory _adapterFactory;
    private readonly IOpportunityRepository _opportunities;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<IngestionOrchestrator> _logger;

    public IngestionOrchestrator(
        ISourceDefinitionRepository sources,
        ISourceAdapterFactory adapterFactory,
        IOpportunityRepository opportunities,
        ITenantContext tenantContext,
        ILogger<IngestionOrchestrator> logger)
    {
        _sources = sources;
        _adapterFactory = adapterFactory;
        _opportunities = opportunities;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<ServiceResult<IngestionRunSummary>> RunForTenantAsync(
        Guid tenantId, CancellationToken ct)
    {
        _tenantContext.SetTenant(tenantId);

        var sources = await _sources.GetEnabledForTenantAsync(tenantId, ct);
        var summary = new IngestionRunSummary();

        foreach (var source in sources)
        {
            var sourceResult = await RunSourceAsync(source, ct);
            summary.Merge(sourceResult);
        }

        await _opportunities.SaveChangesAsync(ct);
        await _sources.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Ingestion run complete for tenant {TenantId}: {Sources} sources, {Ingested} ingested, {Duplicates} duplicates, {Failed} failed",
            tenantId, sources.Count, summary.Ingested, summary.Duplicates, summary.FailedSources);

        return ServiceResult<IngestionRunSummary>.Ok(summary);
    }

    public async Task<SourceRunSummary> RunSourceAsync(SourceDefinition source, CancellationToken ct)
    {
        var summary = new SourceRunSummary(source.Id);
        source.MarkRunStarted();

        IOpportunitySourceAdapter adapter;
        try
        {
            adapter = _adapterFactory.Resolve(source.AdapterType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No adapter registered for {AdapterType} on source {SourceId}",
                source.AdapterType, source.Id);
            source.MarkRunFailed($"No adapter registered for {source.AdapterType}.");
            summary.FailedSources = 1;
            return summary;
        }

        var fetchResult = await adapter.FetchAsync(source, ct);
        if (!fetchResult.IsSuccess)
        {
            _logger.LogWarning("Source {SourceId} fetch failed: {Error}", source.Id, fetchResult.Error);
            source.MarkRunFailed(fetchResult.Error ?? "Unknown failure.");
            summary.FailedSources = 1;
            return summary;
        }

        foreach (var ingested in fetchResult.Value!)
        {
            var existing = await _opportunities.GetBySourceExternalIdAsync(
                source.TenantId, source.Id, ingested.ExternalId, ct);
            if (existing is not null)
            {
                summary.Duplicates++;
                continue;
            }

            var opp = ToOpportunity(ingested, source);
            await _opportunities.AddAsync(opp, ct);
            summary.Ingested++;
        }

        source.MarkRunSucceeded();
        return summary;
    }

    private static Opportunity ToOpportunity(IngestedOpportunity ingested, SourceDefinition source)
    {
        var agency = Agency.Create(ingested.AgencyName, ingested.AgencyType, ingested.AgencyState);
        return Opportunity.Create(
            tenantId: source.TenantId,
            externalId: ingested.ExternalId,
            source: MapAdapterToLegacySource(source.AdapterType),
            title: ingested.Title,
            description: ingested.Description,
            agency: agency,
            postedDate: ingested.PostedDate,
            responseDeadline: ingested.ResponseDeadline,
            naicsCode: ingested.NaicsCode,
            estimatedValue: ingested.EstimatedValue,
            procurementVehicle: ingested.ProcurementVehicle,
            sourceDefinitionId: source.Id);
    }

    private static OpportunitySource MapAdapterToLegacySource(SourceAdapterType adapterType) => adapterType switch
    {
        SourceAdapterType.SamGov => OpportunitySource.SamGov,
        SourceAdapterType.UsaSpending => OpportunitySource.UsaSpending,
        SourceAdapterType.StatePortal => OpportunitySource.StatePortal,
        _ => OpportunitySource.Other
    };
}

public class IngestionRunSummary
{
    public int Ingested { get; set; }
    public int Duplicates { get; set; }
    public int FailedSources { get; set; }

    public void Merge(SourceRunSummary sourceSummary)
    {
        Ingested += sourceSummary.Ingested;
        Duplicates += sourceSummary.Duplicates;
        FailedSources += sourceSummary.FailedSources;
    }
}

public class SourceRunSummary
{
    public Guid SourceDefinitionId { get; }
    public int Ingested { get; set; }
    public int Duplicates { get; set; }
    public int FailedSources { get; set; }

    public SourceRunSummary(Guid sourceId) => SourceDefinitionId = sourceId;
}
