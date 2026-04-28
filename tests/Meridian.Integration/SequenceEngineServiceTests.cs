using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Outreach;
using Meridian.Application.Pipeline;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Outreach;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Integration;

// Verifies SequenceEngineService picks up due enrollments, renders the templated
// snapshot, hands the message to IEmailSender, records the activity, and
// advances the enrollment. Includes a stitched happy-path that runs the
// pipeline THEN the engine — covers the soft-launch milestone.
public class SequenceEngineServiceTests : IDisposable
{
    private readonly PipelineTestFixture _fx = new();
    private readonly InMemoryEmailSender _emails = new();
    public void Dispose() => _fx.Dispose();

    private MeridianDbContext NewDbContext() => _fx.NewDbContext();
    private TenantContext _tenantContext => _fx.TenantContext;

    [Fact]
    public async Task Sends_email_for_due_enrollment_and_advances_step()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var opp = Opportunity.Create(
            tenant.Id, "ext-1", OpportunitySource.SamGov,
            "Contact Center Modernization",
            "Hosted contact center, 200 seats.",
            Agency.Create("Department of Procurement", AgencyType.StateLocal, "UT"),
            DateTimeOffset.UtcNow.AddDays(-1));
        var contact = Contact.Create(
            tenant.Id, "Jordan Buyer", opp.Agency, ContactSource.Manual, 0.9f,
            title: "Procurement Officer", email: "jordan@example.gov");

        var snapshot = SequenceSnapshot.Capture(Guid.NewGuid(), JsonSerializer.Serialize(new[]
        {
            new SequenceStepSnapshot(
                StepNumber: 1, DelayDays: 0,
                Subject: "Re: {{opportunity.title}}",
                BodyTemplate: "Hi {{contact.first_name}}, regarding {{opportunity.title}} at {{agency.name}}.",
                SendWindowStart: TimeSpan.Zero,
                SendWindowEnd: TimeSpan.FromHours(23.99),
                JitterMinutes: 0),
            new SequenceStepSnapshot(2, 3, "Quick follow-up", "Following up on {{opportunity.title}}.",
                TimeSpan.Zero, TimeSpan.FromHours(23.99), 0)
        }));
        var enrollment = OutreachEnrollment.Create(
            tenant.Id, opp.Id, contact.Id, snapshot.SequenceId, snapshot.Id,
            firstSendAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        await using (var seed = NewDbContext())
        {
            seed.Tenants.Add(tenant);
            seed.Opportunities.Add(opp);
            seed.Contacts.Add(contact);
            seed.SequenceSnapshots.Add(snapshot);
            seed.OutreachEnrollments.Add(enrollment);
            await seed.SaveChangesAsync();
        }
        _tenantContext.SetTenant(tenant.Id);

        var sent = await RunEngineAsync(tenant.Id);

        sent.IsSuccess.Should().BeTrue();
        sent.Value.Should().Be(1);

        _emails.Sent.Should().HaveCount(1);
        var msg = _emails.Sent[0];
        msg.To.Should().Be("jordan@example.gov");
        msg.Subject.Should().Be("Re: Contact Center Modernization");
        msg.BodyHtml.Should().Contain("Hi Jordan");
        msg.BodyHtml.Should().Contain("Department of Procurement");

        await using var verify = NewDbContext();
        var advanced = await verify.OutreachEnrollments.FirstAsync(e => e.Id == enrollment.Id);
        advanced.CurrentStep.Should().Be(2, "engine should advance to next step after a successful send");
        advanced.NextSendAt.Should().NotBeNull("step 2 has a delay so NextSendAt must be set");

        var activity = await verify.EmailActivities.FirstAsync();
        activity.EnrollmentId.Should().Be(enrollment.Id);
        activity.StepNumber.Should().Be(1);
        activity.MessageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Skips_enrollment_for_bounced_contact_and_marks_unsubscribed()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var opp = Opportunity.Create(
            tenant.Id, "ext-bounce", OpportunitySource.SamGov, "Test", "desc",
            Agency.Create("Agency", AgencyType.StateLocal),
            DateTimeOffset.UtcNow);
        var bouncedContact = Contact.Create(
            tenant.Id, "Bounced", opp.Agency, ContactSource.Manual, 0.9f,
            email: "bounced@example.gov");
        bouncedContact.MarkBounced();

        var snapshot = SequenceSnapshot.Capture(Guid.NewGuid(), JsonSerializer.Serialize(new[]
        {
            new SequenceStepSnapshot(1, 0, "subj", "body",
                TimeSpan.Zero, TimeSpan.FromHours(23.99), 0)
        }));
        var enrollment = OutreachEnrollment.Create(
            tenant.Id, opp.Id, bouncedContact.Id, snapshot.SequenceId, snapshot.Id,
            firstSendAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        await using (var seed = NewDbContext())
        {
            seed.Tenants.Add(tenant);
            seed.Opportunities.Add(opp);
            seed.Contacts.Add(bouncedContact);
            seed.SequenceSnapshots.Add(snapshot);
            seed.OutreachEnrollments.Add(enrollment);
            await seed.SaveChangesAsync();
        }
        _tenantContext.SetTenant(tenant.Id);

        var sent = await RunEngineAsync(tenant.Id);

        sent.IsSuccess.Should().BeTrue();
        sent.Value.Should().Be(0);
        _emails.Sent.Should().BeEmpty();

        await using var verify = NewDbContext();
        var afterRun = await verify.OutreachEnrollments.FirstAsync(e => e.Id == enrollment.Id);
        afterRun.Status.Should().Be(EnrollmentStatus.Unsubscribed,
            "engine should retire enrollments whose contact has bounced");
    }

    // The end-to-end soft-launch path: ingest a Status=New opportunity, run the
    // pipeline (score → enrich → CRM → enroll), then run the sequence engine.
    // If this test passes, the worker can take an ingested opportunity and
    // produce a sent email without operator intervention.
    [Fact]
    public async Task Stitched_happy_path_pipeline_then_engine_sends_first_email()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var opp = Opportunity.Create(
            tenant.Id, "ext-stitched", OpportunitySource.SamGov,
            "State Contact Center 200-Seat Hosted Solution",
            "RFP for cloud contact center, 200 seats.",
            Agency.Create("State of Utah Procurement", AgencyType.StateLocal, "UT"),
            DateTimeOffset.UtcNow.AddDays(-1));
        var template = OutreachTemplate.Create(
            tenant.Id, "Initial",
            subjectTemplate: "ignored",
            bodyTemplate: "Hi {{contact.first_name}}, re: {{opportunity.title}}.");
        var sequence = OutreachSequence.Create(
            tenant.Id, "MVP Sequence", OpportunityType.Rfp, AgencyType.StateLocal);
        sequence.AddStep(0, template.Id, "Re: {{opportunity.title}}",
            TimeSpan.Zero, TimeSpan.FromHours(23.99), 0);

        await using (var seed = NewDbContext())
        {
            seed.Tenants.Add(tenant);
            seed.Opportunities.Add(opp);
            seed.OutreachTemplates.Add(template);
            seed.OutreachSequences.Add(sequence);
            await seed.SaveChangesAsync();
        }
        _tenantContext.SetTenant(tenant.Id);

        var contact = Contact.Create(
            tenant.Id, "Jordan Buyer", opp.Agency, ContactSource.Manual, 0.9f,
            title: "Procurement Officer", email: "jordan@example.gov");
        var enricher = new StubEnricher(new[] { contact });
        var scoring = new StubScoringEngine(ScoreVerdict.Pursue, total: 8, seats: 200);

        var pipelineResult = await RunPipelineAsync(tenant.Id, scoring, enricher);
        pipelineResult.IsSuccess.Should().BeTrue();
        pipelineResult.Value!.Enrollments.Should().Be(1);

        var sent = await RunEngineAsync(tenant.Id);
        sent.IsSuccess.Should().BeTrue();
        sent.Value.Should().Be(1, "the freshly enrolled contact should receive their first email on the next engine tick");

        _emails.Sent.Should().HaveCount(1);
        _emails.Sent[0].To.Should().Be("jordan@example.gov");
        _emails.Sent[0].Subject.Should().Contain("State Contact Center");
    }

    private async Task<ServiceResult<int>> RunEngineAsync(Guid tenantId)
    {
        await using var db = NewDbContext();
        var engine = new SequenceEngineService(
            new OutreachRepository(db),
            new ContactRepository(db),
            new OpportunityRepository(db),
            _emails,
            new LiquidTemplateRenderer(),
            new AuditLogRepository(db),
            NullLogger<SequenceEngineService>.Instance);
        return await engine.ProcessDueEnrollmentsAsync(tenantId, CancellationToken.None);
    }

    private async Task<ServiceResult<PipelineRunSummary>> RunPipelineAsync(
        Guid tenantId, IScoringEngine scoring, IPocEnricher enricher)
    {
        await using var db = NewDbContext();
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
}
