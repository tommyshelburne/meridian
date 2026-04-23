using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Opportunities;

public class DevSeedService
{
    private readonly IOpportunityRepository _repo;
    private readonly IScoringEngine _scoringEngine;

    public DevSeedService(IOpportunityRepository repo, IScoringEngine scoringEngine)
    {
        _repo = repo;
        _scoringEngine = scoringEngine;
    }

    public async Task<ServiceResult<int>> SeedSampleOpportunitiesAsync(Guid tenantId, CancellationToken ct)
    {
        var samples = BuildSamples(tenantId);
        var added = 0;

        foreach (var (opp, decision) in samples)
        {
            var existing = await _repo.GetByExternalIdAsync(tenantId, opp.ExternalId, ct);
            if (existing is not null) continue;

            var result = _scoringEngine.Score(opp);
            opp.SetSeatEstimate(result.SeatEstimate);
            opp.ApplyScore(result.Score);

            // Pre-decide a few so the Pipeline kanban has cards out of the box,
            // not just the queue. Non-NoBid only — Reject would be misleading.
            switch (decision)
            {
                case PreDecision.Pursue: opp.Pursue(); break;
                case PreDecision.Partner: opp.Partner(); break;
                case PreDecision.Watch: opp.Watch(); break;
                case PreDecision.None: break;
            }

            await _repo.AddAsync(opp, ct);
            added++;
        }

        await _repo.SaveChangesAsync(ct);
        return ServiceResult<int>.Ok(added);
    }

    private enum PreDecision { None, Pursue, Partner, Watch }

    private static IReadOnlyList<(Opportunity Opp, PreDecision Decision)> BuildSamples(Guid tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        return new (Opportunity, PreDecision)[]
        {
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
