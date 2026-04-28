using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Pipeline;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Integration;

// Verifies the post-ingest pipeline picks up Status=New opportunities,
// scores/enriches/CRM-syncs them, and creates enrollments that the SequenceJob
// can then process. This is the soft-launch happy path (spec §7.3 Week 3).
public class MeridianPipelineServiceTests : IDisposable
{
    private readonly PipelineTestFixture _fx = new();
    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Processes_new_opportunity_through_score_enrich_crm_enroll()
    {
        var tenant = Tenant.Create("Acme", "acme");
        await using (var seed = _fx.NewDbContext())
        {
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
        }
        _fx.TenantContext.SetTenant(tenant.Id);

        var opp = NewOpportunity(tenant.Id, "Contact Center Modernization");
        var sequence = NewSequence(tenant.Id);
        var template = OutreachTemplate.Create(tenant.Id, "Initial Outreach", "intro", "Hello {{contact.first_name}}, re: {{opportunity.title}}.");
        sequence.AddStep(0, template.Id, "Re: {{opportunity.title}}", TimeSpan.Zero, TimeSpan.FromHours(23.99), 0);

        await using (var seed = _fx.NewDbContext())
        {
            seed.Opportunities.Add(opp);
            seed.OutreachTemplates.Add(template);
            seed.OutreachSequences.Add(sequence);
            await seed.SaveChangesAsync();
        }

        var contactToReturn = Contact.Create(
            tenant.Id, "Jordan Buyer", opp.Agency, ContactSource.Manual, 0.9f,
            title: "Procurement Officer", email: "jordan@example.gov");
        var enricher = new StubEnricher(new[] { contactToReturn });
        var scoringEngine = new StubScoringEngine(verdict: ScoreVerdict.Pursue, total: 8, seats: 200);

        var summary = await RunPipelineAsync(tenant.Id, scoringEngine, enricher);

        summary.IsSuccess.Should().BeTrue();
        summary.Value!.Processed.Should().Be(1);
        summary.Value.Pursue.Should().Be(1);
        summary.Value.ContactsEnriched.Should().Be(1);
        summary.Value.DealsCreated.Should().Be(1);
        summary.Value.Enrollments.Should().Be(1);

        await using var verify = _fx.NewDbContext();
        var savedOpp = await verify.Opportunities.Include(o => o.Contacts).FirstAsync(o => o.Id == opp.Id);
        savedOpp.Status.Should().Be(OpportunityStatus.Scored);
        savedOpp.Score.Should().NotBeNull();
        savedOpp.Score!.Verdict.Should().Be(ScoreVerdict.Pursue);
        savedOpp.Contacts.Should().HaveCount(1);
        savedOpp.EstimatedSeats.Should().Be(200);

        var enrollment = await verify.OutreachEnrollments.FirstAsync();
        enrollment.OpportunityId.Should().Be(opp.Id);
        enrollment.SequenceId.Should().Be(sequence.Id);
        enrollment.Status.Should().Be(EnrollmentStatus.Active);
        enrollment.NextSendAt.Should().NotBeNull("first send should be queued for the SequenceJob");

        var snapshot = await verify.SequenceSnapshots.FirstAsync(s => s.Id == enrollment.SequenceSnapshotId);
        snapshot.SnapshotJson.Should().Contain("Hello {{contact.first_name}}",
            "snapshot must materialize the template body so later edits don't change in-flight enrollments");

        var auditEvents = await verify.AuditEvents.ToListAsync();
        auditEvents.Should().Contain(a => a.EventType == "OpportunityScored");
        auditEvents.Should().Contain(a => a.EventType == "DealCreated");
        auditEvents.Should().Contain(a => a.EventType == "EnrollmentCreated");
    }

    [Fact]
    public async Task NoBid_verdict_skips_enrichment_and_enrollment()
    {
        var tenant = Tenant.Create("Acme", "acme");
        await using (var seed = _fx.NewDbContext())
        {
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
        }
        _fx.TenantContext.SetTenant(tenant.Id);

        var opp = NewOpportunity(tenant.Id, "Office Furniture RFQ");
        await using (var seed = _fx.NewDbContext())
        {
            seed.Opportunities.Add(opp);
            await seed.SaveChangesAsync();
        }

        var unusedContact = Contact.Create(
            tenant.Id, "Unused", opp.Agency, ContactSource.Manual, 0.9f, email: "unused@example.gov");
        var enricher = new StubEnricher(new[] { unusedContact });
        var scoringEngine = new StubScoringEngine(verdict: ScoreVerdict.NoBid, total: 1, seats: null);

        var summary = await RunPipelineAsync(tenant.Id, scoringEngine, enricher);

        summary.IsSuccess.Should().BeTrue();
        summary.Value!.Processed.Should().Be(0, "no-bid opportunities are not processed past scoring");
        summary.Value.NoBid.Should().Be(1);
        summary.Value.ContactsEnriched.Should().Be(0);
        summary.Value.Enrollments.Should().Be(0);

        enricher.WasCalled.Should().BeFalse("no-bid opportunities must not run enrichment");

        await using var verify = _fx.NewDbContext();
        var savedOpp = await verify.Opportunities.FirstAsync(o => o.Id == opp.Id);
        savedOpp.Status.Should().Be(OpportunityStatus.NoBid);
    }

    [Fact]
    public async Task Pipeline_with_no_sequence_configured_scores_but_does_not_enroll()
    {
        var tenant = Tenant.Create("Acme", "acme");
        await using (var seed = _fx.NewDbContext())
        {
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
        }
        _fx.TenantContext.SetTenant(tenant.Id);

        var opp = NewOpportunity(tenant.Id, "State Contact Center");
        await using (var seed = _fx.NewDbContext())
        {
            seed.Opportunities.Add(opp);
            await seed.SaveChangesAsync();
        }

        var contact = Contact.Create(
            tenant.Id, "Sam POC", opp.Agency, ContactSource.Manual, 0.9f, email: "sam@example.gov");
        var enricher = new StubEnricher(new[] { contact });
        var scoringEngine = new StubScoringEngine(verdict: ScoreVerdict.Pursue, total: 8, seats: 150);

        var summary = await RunPipelineAsync(tenant.Id, scoringEngine, enricher);

        summary.IsSuccess.Should().BeTrue();
        summary.Value!.Processed.Should().Be(1);
        summary.Value.ContactsEnriched.Should().Be(1);
        summary.Value.Enrollments.Should().Be(0, "no sequence configured = no enrollment");

        await using var verify = _fx.NewDbContext();
        (await verify.OutreachEnrollments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Reruns_skip_already_processed_opportunities()
    {
        var tenant = Tenant.Create("Acme", "acme");
        await using (var seed = _fx.NewDbContext())
        {
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
        }
        _fx.TenantContext.SetTenant(tenant.Id);

        var opp = NewOpportunity(tenant.Id, "Re-run Idempotency Test");
        var sequence = NewSequence(tenant.Id);
        var template = OutreachTemplate.Create(tenant.Id, "T", "subj", "body");
        sequence.AddStep(0, template.Id, "subj", TimeSpan.Zero, TimeSpan.FromHours(23.99));

        await using (var seed = _fx.NewDbContext())
        {
            seed.Opportunities.Add(opp);
            seed.OutreachTemplates.Add(template);
            seed.OutreachSequences.Add(sequence);
            await seed.SaveChangesAsync();
        }

        var contact = Contact.Create(
            tenant.Id, "Casey Repeat", opp.Agency, ContactSource.Manual, 0.9f, email: "casey@example.gov");
        var enricher = new StubEnricher(new[] { contact });
        var scoringEngine = new StubScoringEngine(verdict: ScoreVerdict.Pursue, total: 8, seats: 100);

        var first = await RunPipelineAsync(tenant.Id, scoringEngine, enricher);
        first.Value!.Processed.Should().Be(1);

        var second = await RunPipelineAsync(tenant.Id, scoringEngine, enricher);
        second.Value!.Processed.Should().Be(0,
            "scored opportunities have moved past Status=New and shouldn't be picked up again");

        await using var verify = _fx.NewDbContext();
        (await verify.OutreachEnrollments.CountAsync()).Should().Be(1,
            "no duplicate enrollment should be created for the same contact+opportunity pair");
    }

    private async Task<ServiceResult<PipelineRunSummary>> RunPipelineAsync(
        Guid tenantId, IScoringEngine scoring, IPocEnricher enricher)
    {
        await using var db = _fx.NewDbContext();
        var pipeline = new MeridianPipelineService(
            scoring,
            new[] { enricher },
            new StubCrmAdapterFactory(),
            new NullCrmConnectionService(),
            new OpportunityRepository(db),
            new ContactRepository(db),
            new OutreachRepository(db),
            new AuditLogRepository(db),
            NullLogger<MeridianPipelineService>.Instance);

        return await pipeline.ProcessNewOpportunitiesAsync(tenantId, CancellationToken.None);
    }

    private static Opportunity NewOpportunity(Guid tenantId, string title) =>
        Opportunity.Create(
            tenantId,
            externalId: $"ext-{Guid.NewGuid():N}",
            source: OpportunitySource.SamGov,
            title: title,
            description: "RFP for hosted contact center, 200 seats.",
            agency: Agency.Create("Department of Procurement", AgencyType.StateLocal, "UT"),
            postedDate: DateTimeOffset.UtcNow.AddDays(-1));

    private static OutreachSequence NewSequence(Guid tenantId) =>
        OutreachSequence.Create(tenantId, "MVP Sequence", OpportunityType.Rfp, AgencyType.StateLocal);
}

internal class StubScoringEngine : IScoringEngine
{
    private readonly ScoreVerdict _verdict;
    private readonly int _total;
    private readonly int? _seats;

    public StubScoringEngine(ScoreVerdict verdict, int total, int? seats)
    {
        _verdict = verdict;
        _total = total;
        _seats = seats;
    }

    public Domain.Scoring.ScoringResult Score(Opportunity opportunity)
    {
        var score = Domain.Scoring.BidScore.Create(_total, _verdict);
        var seatEstimate = Domain.Scoring.SeatEstimate.Create(
            _seats, _seats >= 100 ? SeatEstimateConfidence.High : SeatEstimateConfidence.Unknown, "stub");
        return new Domain.Scoring.ScoringResult(score, seatEstimate);
    }
}

internal class StubEnricher : IPocEnricher
{
    private readonly IReadOnlyList<Contact> _contacts;
    public bool WasCalled { get; private set; }

    public StubEnricher(IEnumerable<Contact> contacts) => _contacts = contacts.ToList();

    public string SourceName => "stub";

    public Task<ServiceResult<IReadOnlyList<Contact>>> EnrichAsync(
        Opportunity opportunity, Guid tenantId, CancellationToken ct)
    {
        WasCalled = true;
        return Task.FromResult(ServiceResult<IReadOnlyList<Contact>>.Ok(_contacts));
    }
}
