using System.Text.Json;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Audit;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Pipeline;

public class MeridianPipelineService
{
    private readonly IEnumerable<IOpportunitySource> _sources;
    private readonly IScoringEngine _scoringEngine;
    private readonly IEnumerable<IPocEnricher> _enrichers;
    private readonly ICrmClient _crmClient;
    private readonly IOpportunityRepository _opportunityRepo;
    private readonly IContactRepository _contactRepo;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<MeridianPipelineService> _logger;

    public MeridianPipelineService(
        IEnumerable<IOpportunitySource> sources,
        IScoringEngine scoringEngine,
        IEnumerable<IPocEnricher> enrichers,
        ICrmClient crmClient,
        IOpportunityRepository opportunityRepo,
        IContactRepository contactRepo,
        IAuditLog auditLog,
        ILogger<MeridianPipelineService> logger)
    {
        _sources = sources;
        _scoringEngine = scoringEngine;
        _enrichers = enrichers;
        _crmClient = crmClient;
        _opportunityRepo = opportunityRepo;
        _contactRepo = contactRepo;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<ServiceResult<PipelineRunSummary>> ExecuteAsync(Guid tenantId, CancellationToken ct)
    {
        var summary = new PipelineRunSummary();

        // 1. Ingest from all sources in parallel
        var fetchTasks = _sources.Select(s => FetchFromSourceAsync(s, tenantId, ct));
        var results = await Task.WhenAll(fetchTasks);

        var newOpportunities = new List<Opportunity>();
        foreach (var result in results)
        {
            if (result.IsSuccess)
                newOpportunities.AddRange(result.Value!);
            else
                _logger.LogWarning("Source fetch failed: {Error}", result.Error);
        }

        // 2. Deduplicate against existing
        foreach (var opp in newOpportunities)
        {
            var existing = await _opportunityRepo.GetByExternalIdAsync(tenantId, opp.ExternalId, ct);
            if (existing is not null)
            {
                summary.Duplicates++;
                continue;
            }

            await _opportunityRepo.AddAsync(opp, ct);
            summary.Ingested++;

            // 3. Score
            var score = _scoringEngine.Score(opp);
            opp.ApplyScore(score);

            await _auditLog.AppendAsync(AuditEvent.Record(
                tenantId, "Opportunity", opp.Id, "OpportunityScored", "system",
                JsonSerializer.Serialize(new { opp.Title, score.Total, score.Verdict })), ct);

            if (score.Verdict == ScoreVerdict.NoBid)
            {
                summary.NoBid++;
                continue;
            }

            // 4. Enrich POC
            foreach (var enricher in _enrichers)
            {
                var enrichResult = await enricher.EnrichAsync(opp, tenantId, ct);
                if (!enrichResult.IsSuccess) continue;

                foreach (var contact in enrichResult.Value!)
                {
                    var existingContact = contact.Email is not null
                        ? await _contactRepo.GetByEmailAsync(tenantId, contact.Email, ct)
                        : null;

                    if (existingContact is null)
                    {
                        await _contactRepo.AddAsync(contact, ct);
                        opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));
                        summary.ContactsEnriched++;
                    }
                    else
                    {
                        opp.AddContact(OpportunityContact.Create(opp.Id, existingContact.Id));
                    }
                }

                if (opp.Contacts.Count > 0) break;
            }

            // 5. Create CRM deal for Pursue/Partner
            var orgResult = await _crmClient.FindOrCreateOrganizationAsync(opp.Agency.Name, ct);
            if (orgResult.IsSuccess)
            {
                var dealResult = await _crmClient.CreateDealAsync(opp, orgResult.Value!, ct);
                if (dealResult.IsSuccess)
                {
                    summary.DealsCreated++;
                    await _auditLog.AppendAsync(AuditEvent.Record(
                        tenantId, "Opportunity", opp.Id, "DealCreated", "system",
                        JsonSerializer.Serialize(new { DealId = dealResult.Value, opp.Title })), ct);
                }
            }

            if (score.Verdict == ScoreVerdict.Pursue) summary.Pursue++;
            else summary.Partner++;
        }

        await _opportunityRepo.SaveChangesAsync(ct);
        await _contactRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Pipeline run complete: {Summary}", summary);
        return ServiceResult<PipelineRunSummary>.Ok(summary);
    }

    private async Task<ServiceResult<IReadOnlyList<Opportunity>>> FetchFromSourceAsync(
        IOpportunitySource source, Guid tenantId, CancellationToken ct)
    {
        try
        {
            return await source.FetchOpportunitiesAsync(tenantId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch from {Source}", source.SourceName);
            return ServiceResult<IReadOnlyList<Opportunity>>.Fail($"{source.SourceName}: {ex.Message}");
        }
    }
}

public record PipelineRunSummary
{
    public int Ingested { get; set; }
    public int Duplicates { get; set; }
    public int Pursue { get; set; }
    public int Partner { get; set; }
    public int NoBid { get; set; }
    public int ContactsEnriched { get; set; }
    public int DealsCreated { get; set; }
}
