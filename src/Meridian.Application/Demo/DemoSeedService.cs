using System.Text.Json;
using Meridian.Application.Common;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;
using Meridian.Domain.Outreach;

namespace Meridian.Application.Demo;

// Seeds a demo tenant with a complete, internally-consistent story: scored
// opportunities spread across the pipeline board, contacts at the relevant
// agencies, two sequences with steps, and enrollments with backdated email
// history (including a reply) so every screen in the guided demo has real
// data on it. Idempotent — the MDEMO-NEW sentinel short-circuits a re-run
// so reset/provision can call it unconditionally.
//
// All contact emails use RFC 2606 reserved domains as defense in depth on
// top of the Console outbound provider: even if the provider were flipped,
// nothing seeded here is deliverable.
public class DemoSeedService
{
    public const string SentinelExternalId = "MDEMO-NEW";

    private readonly IOpportunityRepository _opportunities;
    private readonly IContactRepository _contacts;
    private readonly IOutreachRepository _outreach;
    private readonly IOutboundConfigurationRepository _outboundConfigs;
    private readonly IScoringEngine _scoringEngine;

    public DemoSeedService(
        IOpportunityRepository opportunities,
        IContactRepository contacts,
        IOutreachRepository outreach,
        IOutboundConfigurationRepository outboundConfigs,
        IScoringEngine scoringEngine)
    {
        _opportunities = opportunities;
        _contacts = contacts;
        _outreach = outreach;
        _outboundConfigs = outboundConfigs;
        _scoringEngine = scoringEngine;
    }

    public async Task<ServiceResult<DemoSeedSummary>> SeedAsync(Guid tenantId, CancellationToken ct)
    {
        var sentinel = await _opportunities.GetByExternalIdAsync(tenantId, SentinelExternalId, ct);
        if (sentinel is not null)
            return ServiceResult<DemoSeedSummary>.Ok(new DemoSeedSummary(0, 0, 0, 0, 0, 0));

        await EnsureOutboundConfigAsync(tenantId, ct);

        var (introTemplate, followUpTemplate) = await SeedTemplatesAsync(tenantId, ct);
        var (federalSequence, stateSequence) = await SeedSequencesAsync(
            tenantId, introTemplate, followUpTemplate, ct);

        var contacts = SeedContacts(tenantId);
        foreach (var contact in contacts.Values)
            await _contacts.AddAsync(contact, ct);

        var opportunities = SeedOpportunities(tenantId, contacts);
        foreach (var opportunity in opportunities.Values)
            await _opportunities.AddAsync(opportunity, ct);

        var templatesById = new Dictionary<Guid, OutreachTemplate>
        {
            [introTemplate.Id] = introTemplate,
            [followUpTemplate.Id] = followUpTemplate,
        };
        var (enrollments, activities) = await SeedOutreachHistoryAsync(
            tenantId, opportunities, contacts, federalSequence, stateSequence, templatesById, ct);

        await _opportunities.SaveChangesAsync(ct);

        return ServiceResult<DemoSeedSummary>.Ok(new DemoSeedSummary(
            opportunities.Count, contacts.Count, Templates: 2, Sequences: 2,
            enrollments, activities));
    }

    private async Task EnsureOutboundConfigAsync(Guid tenantId, CancellationToken ct)
    {
        var existing = await _outboundConfigs.GetByTenantIdAsync(tenantId, ct);
        if (existing is not null) return;

        // Console provider is the sandbox: the sequence engine runs the full
        // send path and records EmailActivity, but the "send" is a log line.
        var config = OutboundConfiguration.Create(
            tenantId,
            OutboundProviderType.Console,
            encryptedApiKey: string.Empty,
            fromAddress: "demo@example.com",
            fromName: "Meridian Demo",
            physicalAddress: "2200 S Main St, Salt Lake City, UT 84115",
            unsubscribeBaseUrl: "https://meridianbd.dev/unsubscribe");
        await _outboundConfigs.AddAsync(config, ct);
    }

    private async Task<(OutreachTemplate Intro, OutreachTemplate FollowUp)> SeedTemplatesAsync(
        Guid tenantId, CancellationToken ct)
    {
        var intro = OutreachTemplate.Create(
            tenantId,
            name: "Intro — Modernization Outreach",
            subjectTemplate: "Re: {{opportunity.title}}",
            bodyTemplate:
                "Hi {{contact.first_name}},\n\n" +
                "I saw {{agency.name}}'s posting for \"{{opportunity.title}}\" and wanted to reach out. " +
                "We've helped agencies with similar modernization efforts cut average handle time " +
                "while keeping accessibility and compliance requirements front and center.\n\n" +
                "Would a brief call this week be useful as you shape the requirement?\n");
        await _outreach.AddTemplateAsync(intro, ct);

        var followUp = OutreachTemplate.Create(
            tenantId,
            name: "Follow-up — Quick Question",
            subjectTemplate: "Following up: {{opportunity.title}}",
            bodyTemplate:
                "Hi {{contact.first_name}},\n\n" +
                "Following up on my earlier note about \"{{opportunity.title}}\". " +
                "One quick question: is the incumbent expected to recompete? " +
                "Happy to share a one-pager on how we approach transitions either way.\n");
        await _outreach.AddTemplateAsync(followUp, ct);

        return (intro, followUp);
    }

    private async Task<(OutreachSequence Federal, OutreachSequence StateLocal)> SeedSequencesAsync(
        Guid tenantId, OutreachTemplate intro, OutreachTemplate followUp, CancellationToken ct)
    {
        var businessHoursStart = TimeSpan.FromHours(9);
        var businessHoursEnd = TimeSpan.FromHours(17);

        var federal = OutreachSequence.Create(
            tenantId,
            name: "Federal RFP Outreach",
            opportunityType: OpportunityType.Rfp,
            agencyType: AgencyType.FederalCivilian);
        federal.AddStep(0, intro.Id, subject: string.Empty,
            businessHoursStart, businessHoursEnd, jitterMinutes: 20);
        federal.AddStep(3, followUp.Id, subject: string.Empty,
            businessHoursStart, businessHoursEnd, jitterMinutes: 20);
        federal.AddStep(7, followUp.Id, subject: "Last note: {{opportunity.title}}",
            businessHoursStart, businessHoursEnd, jitterMinutes: 20);
        await _outreach.AddSequenceAsync(federal, ct);

        var stateLocal = OutreachSequence.Create(
            tenantId,
            name: "State & Local Outreach",
            opportunityType: OpportunityType.Rfp,
            agencyType: AgencyType.StateLocal);
        stateLocal.AddStep(0, intro.Id, subject: string.Empty,
            businessHoursStart, businessHoursEnd, jitterMinutes: 20);
        stateLocal.AddStep(4, followUp.Id, subject: string.Empty,
            businessHoursStart, businessHoursEnd, jitterMinutes: 20);
        await _outreach.AddSequenceAsync(stateLocal, ct);

        return (federal, stateLocal);
    }

    private static Dictionary<string, Contact> SeedContacts(Guid tenantId)
    {
        return new Dictionary<string, Contact>
        {
            ["maria"] = Contact.Create(tenantId, "Maria Vasquez",
                Agency.Create("Department of Veterans Affairs", AgencyType.FederalCivilian),
                ContactSource.SamGov, 0.92f, title: "Contracting Officer",
                email: "maria.vasquez@example.com"),
            ["dan"] = Contact.Create(tenantId, "Dan Whitfield",
                Agency.Create("Colorado Office of Information Technology", AgencyType.StateLocal, state: "CO"),
                ContactSource.UsaSpending, 0.85f, title: "IT Procurement Lead",
                email: "dan.whitfield@example.org"),
            ["priya"] = Contact.Create(tenantId, "Priya Natarajan",
                Agency.Create("General Services Administration", AgencyType.FederalCivilian),
                ContactSource.AgencyDirectory, 0.78f, title: "Program Manager, Citizen Experience",
                email: "priya.natarajan@example.com"),
            ["james"] = Contact.Create(tenantId, "James Okafor",
                Agency.Create("Accenture Federal Solutions", AgencyType.PrimeContractor),
                ContactSource.LinkedIn, 0.88f, title: "Capture Manager",
                email: "james.okafor@example.net",
                linkedInUrl: "https://www.linkedin.com/in/example-james-okafor"),
            ["sue"] = Contact.Create(tenantId, "Sue Hartley",
                Agency.Create("Texas Department of Information Resources", AgencyType.StateLocal, state: "TX"),
                ContactSource.AgencyDirectory, 0.66f, title: "Director, Shared Services",
                email: "sue.hartley@example.org"),
            ["al"] = Contact.Create(tenantId, "Al Reyes",
                Agency.Create("Defense Logistics Agency", AgencyType.FederalDefense),
                ContactSource.SamGov, 0.74f, title: "Helpdesk Modernization Lead",
                email: "al.reyes@example.com"),
        };
    }

    private Dictionary<string, Opportunity> SeedOpportunities(
        Guid tenantId, IReadOnlyDictionary<string, Contact> contacts)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<string, Opportunity>();

        // Left untouched (Status=New) on purpose: the live-demo beat runs the
        // ProcessingJob against it so the prospect watches Meridian score,
        // enrich, and enroll an opportunity in real time.
        result["fresh"] = Opportunity.Create(tenantId, SentinelExternalId, OpportunitySource.SamGov,
            "City of Phoenix 311 Contact Center Replacement — 150 seats",
            "The City of Phoenix seeks proposals to replace its aging 311 citizen contact center platform with a cloud solution including IVR modernization, AI-assisted call routing, and bilingual self-service. Estimated 150 seats across two sites.",
            Agency.Create("City of Phoenix", AgencyType.StateLocal, state: "AZ"),
            postedDate: now.AddHours(-3),
            responseDeadline: now.AddDays(32),
            naicsCode: "541512",
            estimatedValue: 5_200_000m,
            procurementVehicle: ProcurementVehicle.StateCooperative);

        result["va"] = Decide(Score(Opportunity.Create(tenantId, "MDEMO-001", OpportunitySource.SamGov,
            "VA Helpdesk Contact Center Modernization — 500 agents",
            "The Department of Veterans Affairs seeks a contractor to modernize its national helpdesk contact center: IVR replacement, self-service expansion, and AI-powered routing across three locations. Recompete of VA-456-CC-2021; estimated 500 agents.",
            Agency.Create("Department of Veterans Affairs", AgencyType.FederalCivilian),
            postedDate: now.AddDays(-6),
            responseDeadline: now.AddDays(26),
            naicsCode: "561422",
            estimatedValue: 14_500_000m,
            procurementVehicle: ProcurementVehicle.GsaEbuy)), o => o.Pursue());
        Attach(result["va"], contacts["maria"], isPrimary: true);

        result["colorado"] = Decide(Score(Opportunity.Create(tenantId, "MDEMO-002", OpportunitySource.UsaSpending,
            "Colorado Unemployment Claims IVR Replacement",
            "Colorado OIT seeks a conversational AI platform to replace the legacy unemployment claims IVR. Roughly 140 agents; English and Spanish natural-language routing required.",
            Agency.Create("Colorado Office of Information Technology", AgencyType.StateLocal, state: "CO"),
            postedDate: now.AddDays(-9),
            responseDeadline: now.AddDays(19),
            naicsCode: "541512",
            estimatedValue: 2_900_000m,
            procurementVehicle: ProcurementVehicle.StateCooperative)), o => o.Pursue());
        Attach(result["colorado"], contacts["dan"], isPrimary: true);

        result["gsa"] = Decide(Score(Opportunity.Create(tenantId, "MDEMO-003", OpportunitySource.SamGov,
            "GSA Citizen Experience Helpdesk Consolidation",
            "GSA seeks to consolidate three regional helpdesks into a single modern customer service platform with chatbot-first deflection and accessibility-compliant self-service.",
            Agency.Create("General Services Administration", AgencyType.FederalCivilian),
            postedDate: now.AddDays(-12),
            responseDeadline: now.AddDays(40),
            naicsCode: "541512",
            estimatedValue: 7_800_000m,
            procurementVehicle: ProcurementVehicle.GsaSchedule)), o => o.Approve());
        Attach(result["gsa"], contacts["priya"], isPrimary: true);

        result["ssa"] = Decide(Score(Opportunity.Create(tenantId, "MDEMO-004", OpportunitySource.Manual,
            "Teaming — SSA Claims Contact Center Transformation (prime: Accenture Federal)",
            "Accenture Federal seeks teaming partners for the Social Security Administration's AI-powered claims contact center transformation. Estimated 1000+ seats over three years; partner scope covers conversational AI and agent assist.",
            Agency.Create("Accenture Federal Solutions", AgencyType.PrimeContractor),
            postedDate: now.AddDays(-5),
            responseDeadline: now.AddDays(13),
            naicsCode: "541512",
            estimatedValue: 45_000_000m,
            procurementVehicle: ProcurementVehicle.OpenMarket)), o => o.Partner());
        Attach(result["ssa"], contacts["james"], isPrimary: true);

        result["dla"] = Decide(Score(Opportunity.Create(tenantId, "MDEMO-005", OpportunitySource.SamGov,
            "DLA Customer Support Modernization — 250 seats, CMMC L2",
            "Defense Logistics Agency requires modernization of its customer support helpdesk, replacing the existing on-prem platform. 250 seats; CMMC Level 2 required at award.",
            Agency.Create("Defense Logistics Agency", AgencyType.FederalDefense),
            postedDate: now.AddDays(-4),
            responseDeadline: now.AddDays(55),
            naicsCode: "541512",
            estimatedValue: 8_700_000m,
            procurementVehicle: ProcurementVehicle.GsaSchedule)), o => o.Watch());
        Attach(result["dla"], contacts["al"]);

        result["janitorial"] = Decide(Score(Opportunity.Create(tenantId, "MDEMO-006", OpportunitySource.Other,
            "Janitorial Services — Federal Building Annex",
            "GSA seeks a janitorial services contractor for the federal building annex. Five-year IDIQ.",
            Agency.Create("General Services Administration", AgencyType.FederalCivilian),
            postedDate: now.AddDays(-2),
            responseDeadline: now.AddDays(11),
            naicsCode: "561720",
            estimatedValue: 320_000m,
            procurementVehicle: ProcurementVehicle.OpenMarket)), o => o.Reject());

        result["texas"] = Decide(Score(Opportunity.Create(tenantId, "MDEMO-007", OpportunitySource.UsaSpending,
            "Texas DIR Citizen Service Platform Refresh — multi-agency",
            "Texas DIR seeks cooperative-vehicle responses for modernization of citizen-facing customer service platforms across participating agencies; estimated 80-100 seats per agency.",
            Agency.Create("Texas Department of Information Resources", AgencyType.StateLocal, state: "TX"),
            postedDate: now.AddDays(-11),
            responseDeadline: now.AddDays(34),
            naicsCode: "518210",
            estimatedValue: 4_800_000m,
            procurementVehicle: ProcurementVehicle.StateCooperative)), o => o.Approve());
        Attach(result["texas"], contacts["sue"], isPrimary: true);

        result["florida"] = Decide(Score(Opportunity.Create(tenantId, "MDEMO-008", OpportunitySource.SamGov,
            "Florida DMV Virtual Agent & Call Deflection Pilot",
            "Florida HSMV seeks a virtual agent pilot for licensing and registration inquiries with measured call deflection targets and a path to statewide rollout.",
            Agency.Create("Florida Department of Highway Safety and Motor Vehicles", AgencyType.StateLocal, state: "FL"),
            postedDate: now.AddDays(-8),
            responseDeadline: now.AddDays(23),
            naicsCode: "541512",
            estimatedValue: 1_600_000m,
            procurementVehicle: ProcurementVehicle.OpenMarket)), o => o.Watch());

        return result;
    }

    private Opportunity Score(Opportunity opportunity)
    {
        var result = _scoringEngine.Score(opportunity);
        opportunity.SetSeatEstimate(result.SeatEstimate);
        opportunity.ApplyScore(result.Score);
        return opportunity;
    }

    private static Opportunity Decide(Opportunity opportunity, Action<Opportunity> decision)
    {
        decision(opportunity);
        return opportunity;
    }

    private static void Attach(Opportunity opportunity, Contact contact, bool isPrimary = false)
        => opportunity.AddContact(OpportunityContact.Create(opportunity.Id, contact.Id, isPrimary));

    // Backdated outreach history: one mid-sequence enrollment, one reply (the
    // money moment for the replies view), one completed run, and one queued
    // first send — five activities total.
    private async Task<(int Enrollments, int Activities)> SeedOutreachHistoryAsync(
        Guid tenantId,
        IReadOnlyDictionary<string, Opportunity> opportunities,
        IReadOnlyDictionary<string, Contact> contacts,
        OutreachSequence federal,
        OutreachSequence stateLocal,
        IReadOnlyDictionary<Guid, OutreachTemplate> templatesById,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var federalSnapshot = await CaptureSnapshotAsync(federal, templatesById, ct);
        var stateSnapshot = await CaptureSnapshotAsync(stateLocal, templatesById, ct);

        // Maria @ VA — step 1 sent three days ago, step 2 due in two days.
        var active = OutreachEnrollment.Create(tenantId, opportunities["va"].Id,
            contacts["maria"].Id, federal.Id, federalSnapshot.Id, firstSendAt: now.AddDays(-3));
        await _outreach.AddEnrollmentAsync(active, ct);
        await _outreach.AddEmailActivityAsync(EmailActivity.Record(tenantId, active.Id,
            contacts["maria"].Id, opportunities["va"].Id, stepNumber: 1,
            subject: "Re: VA Helpdesk Contact Center Modernization — 500 agents",
            bodyText: "Hi Maria,\n\nI saw the Department of Veterans Affairs' posting for the helpdesk modernization and wanted to reach out...",
            messageId: "demo-va-step1", sentAt: now.AddDays(-3)), ct);
        active.AdvanceStep(nextSendAt: now.AddDays(2), totalSteps: 3);

        // Dan @ Colorado — replied two days ago. MarkReplied stops the sequence.
        var replied = OutreachEnrollment.Create(tenantId, opportunities["colorado"].Id,
            contacts["dan"].Id, stateLocal.Id, stateSnapshot.Id, firstSendAt: now.AddDays(-5));
        await _outreach.AddEnrollmentAsync(replied, ct);
        var repliedActivity = EmailActivity.Record(tenantId, replied.Id,
            contacts["dan"].Id, opportunities["colorado"].Id, stepNumber: 1,
            subject: "Re: Colorado Unemployment Claims IVR Replacement",
            bodyText: "Hi Dan,\n\nI saw Colorado OIT's posting for the claims IVR replacement and wanted to reach out...",
            messageId: "demo-co-step1", sentAt: now.AddDays(-5));
        repliedActivity.RecordReply(now.AddDays(-2),
            "Thanks for reaching out — we're finalizing requirements ahead of the RFP. Could you do a short call Thursday afternoon?");
        await _outreach.AddEmailActivityAsync(repliedActivity, ct);
        replied.MarkReplied();

        // Priya @ GSA — full three-step sequence completed without a reply.
        var completed = OutreachEnrollment.Create(tenantId, opportunities["gsa"].Id,
            contacts["priya"].Id, federal.Id, federalSnapshot.Id, firstSendAt: now.AddDays(-10));
        await _outreach.AddEnrollmentAsync(completed, ct);
        for (var step = 1; step <= 3; step++)
        {
            await _outreach.AddEmailActivityAsync(EmailActivity.Record(tenantId, completed.Id,
                contacts["priya"].Id, opportunities["gsa"].Id, stepNumber: step,
                subject: step == 1
                    ? "Re: GSA Citizen Experience Helpdesk Consolidation"
                    : "Following up: GSA Citizen Experience Helpdesk Consolidation",
                bodyText: "Hi Priya,\n\n(step " + step + " of the federal outreach sequence)...",
                messageId: $"demo-gsa-step{step}", sentAt: now.AddDays(-10 + (step - 1) * 3)), ct);
            completed.AdvanceStep(nextSendAt: now.AddDays(-10 + step * 3), totalSteps: 3);
        }

        // James @ SSA teaming — enrolled, first send queued for the near future
        // so the demo can show a scheduled outbound email.
        var queued = OutreachEnrollment.Create(tenantId, opportunities["ssa"].Id,
            contacts["james"].Id, federal.Id, federalSnapshot.Id, firstSendAt: now.AddHours(4));
        await _outreach.AddEnrollmentAsync(queued, ct);

        return (Enrollments: 4, Activities: 5);
    }

    // Same materialization OutreachEnrollmentService performs at enrollment
    // time: subjects fall back to the template's when the step has no override.
    // Templates are resolved in-memory because nothing is saved yet here.
    private async Task<SequenceSnapshot> CaptureSnapshotAsync(
        OutreachSequence sequence,
        IReadOnlyDictionary<Guid, OutreachTemplate> templatesById,
        CancellationToken ct)
    {
        var steps = new List<SequenceStepSnapshot>(sequence.Steps.Count);
        foreach (var step in sequence.Steps.OrderBy(s => s.StepNumber))
        {
            var template = templatesById.GetValueOrDefault(step.TemplateId);
            var subject = string.IsNullOrWhiteSpace(step.Subject)
                ? template?.SubjectTemplate ?? string.Empty
                : step.Subject;
            steps.Add(new SequenceStepSnapshot(
                step.StepNumber, step.DelayDays, subject, template?.BodyTemplate ?? string.Empty,
                step.SendWindowStart, step.SendWindowEnd, step.SendWindowJitterMinutes));
        }

        var snapshot = SequenceSnapshot.Capture(sequence.Id, JsonSerializer.Serialize(steps));
        await _outreach.AddSnapshotAsync(snapshot, ct);
        return snapshot;
    }
}
