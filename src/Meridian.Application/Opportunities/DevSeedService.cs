using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;

namespace Meridian.Application.Opportunities;

public class DevSeedService
{
    private readonly IOpportunityRepository _repo;
    private readonly IScoringEngine _scoringEngine;
    private readonly IOutreachRepository _outreachRepo;
    private readonly IOutboundConfigurationRepository _outboundConfigRepo;

    public DevSeedService(
        IOpportunityRepository repo,
        IScoringEngine scoringEngine,
        IOutreachRepository outreachRepo,
        IOutboundConfigurationRepository outboundConfigRepo)
    {
        _repo = repo;
        _scoringEngine = scoringEngine;
        _outreachRepo = outreachRepo;
        _outboundConfigRepo = outboundConfigRepo;
    }

    public async Task<ServiceResult<int>> SeedSampleOpportunitiesAsync(Guid tenantId, CancellationToken ct)
    {
        var samples = BuildSamples(tenantId);
        var added = 0;

        foreach (var (opp, decision) in samples)
        {
            var existing = await _repo.GetByExternalIdAsync(tenantId, opp.ExternalId, ct);
            if (existing is not null) continue;

            // The "Soft launch dry run" sample is left at Status=New so the
            // ProcessingJob picks it up on its next tick. The other samples are
            // pre-decided so the Pipeline kanban has cards out of the box.
            if (decision != PreDecision.Untouched)
            {
                var result = _scoringEngine.Score(opp);
                opp.SetSeatEstimate(result.SeatEstimate);
                opp.ApplyScore(result.Score);

                switch (decision)
                {
                    case PreDecision.Pursue: opp.Pursue(); break;
                    case PreDecision.Partner: opp.Partner(); break;
                    case PreDecision.Watch: opp.Watch(); break;
                    case PreDecision.None: break;
                }
            }

            await _repo.AddAsync(opp, ct);
            added++;
        }

        await _repo.SaveChangesAsync(ct);
        return ServiceResult<int>.Ok(added);
    }

    // Seeds the bare minimum a tenant needs to fire the post-ingest pipeline
    // end-to-end in dev: a Console-provider OutboundConfiguration + an
    // OutreachTemplate + an OutreachSequence with one immediate step. Idempotent.
    public async Task<ServiceResult<bool>> SeedOutreachScaffoldAsync(Guid tenantId, CancellationToken ct)
    {
        var existingConfig = await _outboundConfigRepo.GetByTenantIdAsync(tenantId, ct);
        if (existingConfig is null)
        {
            var config = OutboundConfiguration.Create(
                tenantId,
                OutboundProviderType.Console,
                encryptedApiKey: string.Empty,
                fromAddress: "noreply@meridian.local",
                fromName: "Meridian (dev)",
                physicalAddress: "100 Dev Way, Localhost, UT 84000",
                unsubscribeBaseUrl: "https://localhost:5001/unsubscribe");
            await _outboundConfigRepo.AddAsync(config, ct);
            await _outboundConfigRepo.SaveChangesAsync(ct);
        }

        var existingSequences = await _outreachRepo.GetSequencesAsync(tenantId, ct);
        if (existingSequences.Count > 0) return ServiceResult<bool>.Ok(true);

        var template = OutreachTemplate.Create(
            tenantId,
            name: "Initial Outreach (dev)",
            subjectTemplate: "Re: {{opportunity.title}}",
            bodyTemplate:
                "Hi {{contact.first_name}},\n\n" +
                "I came across {{agency.name}}'s posting for \"{{opportunity.title}}\". " +
                "Happy to share how we've helped similar agencies; brief call this week?\n\n" +
                "— Meridian (dev)\n");
        await _outreachRepo.AddTemplateAsync(template, ct);

        var sequence = OutreachSequence.Create(
            tenantId,
            name: "Default Outreach Sequence (dev)",
            opportunityType: OpportunityType.Rfp,
            agencyType: AgencyType.StateLocal);
        sequence.AddStep(
            delayDays: 0,
            templateId: template.Id,
            subject: "Re: {{opportunity.title}}",
            sendWindowStart: TimeSpan.Zero,
            sendWindowEnd: TimeSpan.FromHours(23.99),
            jitterMinutes: 0);
        await _outreachRepo.AddSequenceAsync(sequence, ct);
        await _outreachRepo.SaveChangesAsync(ct);

        return ServiceResult<bool>.Ok(true);
    }

    private enum PreDecision { None, Pursue, Partner, Watch, Untouched }

    private static IReadOnlyList<(Opportunity Opp, PreDecision Decision)> BuildSamples(Guid tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        return new (Opportunity, PreDecision)[]
        {
            // The soft-launch dry-run target: ingested but not yet processed.
            // ProcessingJob will pick this up, score it, enrich it (no-op
            // without real enrichers wired), and — if SeedOutreachScaffold has
            // been called — enroll it for the SequenceJob to send.
            (Opportunity.Create(tenantId, "DEMO-SOFT-LAUNCH", OpportunitySource.Manual,
                "State of Utah Contact Center Modernization — 200 seats",
                "Utah DTS issued an RFP for a hosted contact center to replace the existing on-prem Avaya deployment across DWS and DCFS. Estimated 200 seats; 5-year base + options.",
                Agency.Create("Utah Department of Technology Services", AgencyType.StateLocal, state: "UT"),
                postedDate: now.AddHours(-2),
                responseDeadline: now.AddDays(30),
                naicsCode: "541512",
                estimatedValue: 6_500_000m,
                procurementVehicle: ProcurementVehicle.StateCooperative), PreDecision.Untouched),

            (Opportunity.Create(tenantId, "DEMO-001", OpportunitySource.SamGov,
                "Contact Center Modernization for VA Helpdesk — 500 agents",
                "The Department of Veterans Affairs seeks a contractor to modernize its existing contact center, including IVR replacement (currently Nuance), self-service expansion, and AI-powered call routing. Recompete of contract VA-456-CC-2021. Estimated 500 agents across three locations.",
                Agency.Create("Department of Veterans Affairs", AgencyType.FederalCivilian),
                postedDate: now.AddDays(-3),
                responseDeadline: now.AddDays(28),
                naicsCode: "561422",
                estimatedValue: 14_500_000m,
                procurementVehicle: ProcurementVehicle.GsaEbuy), PreDecision.Pursue),

            (Opportunity.Create(tenantId, "DEMO-002", OpportunitySource.UsaSpending,
                "Citizen Services IVR Replacement — Utah Department of Workforce Services",
                "Replace existing legacy IVR with modern conversational AI platform. Approximately 120 agents handle citizen unemployment claims; system must support Spanish + English with natural language routing.",
                Agency.Create("Utah DWS", AgencyType.StateLocal, state: "UT"),
                postedDate: now.AddDays(-7),
                responseDeadline: now.AddDays(21),
                naicsCode: "541512",
                estimatedValue: 2_400_000m,
                procurementVehicle: ProcurementVehicle.StateCooperative), PreDecision.None),

            (Opportunity.Create(tenantId, "DEMO-003", OpportunitySource.SamGov,
                "DLA Customer Support Modernization — 250 seats",
                "Defense Logistics Agency requires modernization of customer support helpdesk. Replace existing Avaya platform; 250 seats; CMMC Level 2 required.",
                Agency.Create("Defense Logistics Agency", AgencyType.FederalDefense),
                postedDate: now.AddDays(-2),
                responseDeadline: now.AddDays(45),
                naicsCode: "541512",
                estimatedValue: 8_700_000m,
                procurementVehicle: ProcurementVehicle.GsaSchedule), PreDecision.Watch),

            (Opportunity.Create(tenantId, "DEMO-004", OpportunitySource.Manual,
                "Teaming opportunity — Accenture Federal SSA modernization",
                "Accenture Federal seeks teaming partners for Social Security Administration AI-powered claims contact center transformation. Estimated 1000+ seats deployment over 3 years.",
                Agency.Create("Accenture Federal Solutions", AgencyType.PrimeContractor),
                postedDate: now.AddDays(-5),
                responseDeadline: now.AddDays(14),
                naicsCode: "541512",
                estimatedValue: 45_000_000m,
                procurementVehicle: ProcurementVehicle.OpenMarket), PreDecision.Partner),

            (Opportunity.Create(tenantId, "DEMO-005", OpportunitySource.Other,
                "Janitorial Services — Federal Building Annex",
                "GSA seeks janitorial services contractor for federal building annex.",
                Agency.Create("General Services Administration", AgencyType.FederalCivilian),
                postedDate: now.AddDays(-1),
                responseDeadline: now.AddDays(10),
                naicsCode: "561720",
                estimatedValue: 320_000m,
                procurementVehicle: ProcurementVehicle.OpenMarket), PreDecision.None),

            (Opportunity.Create(tenantId, "DEMO-006", OpportunitySource.UsaSpending,
                "Texas DIR Customer Service Platform Refresh",
                "Texas Department of Information Resources seeks responses for cooperative-vehicle modernization of citizen-facing customer service platform. Multi-agency, estimated 80-100 seats per agency.",
                Agency.Create("Texas DIR", AgencyType.StateLocal, state: "TX"),
                postedDate: now.AddDays(-10),
                responseDeadline: now.AddDays(35),
                naicsCode: "518210",
                estimatedValue: 4_800_000m,
                procurementVehicle: ProcurementVehicle.StateCooperative), PreDecision.None),
        };
    }
}
