using FluentAssertions;
using Meridian.Application.Outreach;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Outreach;

namespace Meridian.Unit.Application.Outreach;

public class OutreachSequenceServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task CreateTemplateAsync_persists_template_and_returns_id()
    {
        var repo = new FakeOutreachRepository();
        var svc = new OutreachSequenceService(repo);

        var result = await svc.CreateTemplateAsync(
            _tenantId,
            new CreateTemplateRequest("Initial", "Re: {{opportunity.title}}", "Hi {{contact.first_name}}"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        repo.Templates.Should().HaveCount(1);
        repo.Templates.Single().Name.Should().Be("Initial");
        repo.SaveCount.Should().Be(1);
    }

    [Theory]
    [InlineData("", "subj", "body", "Template name is required.")]
    [InlineData("name", "  ", "body", "Subject template is required.")]
    [InlineData("name", "subj", "", "Body template is required.")]
    public async Task CreateTemplateAsync_rejects_blank_fields(string name, string subj, string body, string expectedError)
    {
        var svc = new OutreachSequenceService(new FakeOutreachRepository());
        var result = await svc.CreateTemplateAsync(
            _tenantId, new CreateTemplateRequest(name, subj, body), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(expectedError);
    }

    [Fact]
    public async Task CreateSequenceAsync_persists_with_steps_in_order()
    {
        var repo = new FakeOutreachRepository();
        var template = OutreachTemplate.Create(_tenantId, "T1", "subj", "body");
        repo.Templates.Add(template);
        var svc = new OutreachSequenceService(repo);

        var result = await svc.CreateSequenceAsync(_tenantId,
            new CreateSequenceRequest("MVP", OpportunityType.Rfp, AgencyType.StateLocal,
                new[]
                {
                    new CreateSequenceStepRequest(0, template.Id, "Re: A",
                        TimeSpan.FromHours(14), TimeSpan.FromHours(22), 0),
                    new CreateSequenceStepRequest(3, template.Id, "Follow-up",
                        TimeSpan.FromHours(14), TimeSpan.FromHours(22), 5)
                }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repo.Sequences.Should().HaveCount(1);
        var seq = repo.Sequences.Single();
        seq.Steps.Should().HaveCount(2);
        seq.Steps.OrderBy(s => s.StepNumber).Select(s => s.DelayDays)
            .Should().Equal(0, 3);
    }

    [Fact]
    public async Task CreateSequenceAsync_rejects_when_referenced_template_missing()
    {
        var svc = new OutreachSequenceService(new FakeOutreachRepository());
        var result = await svc.CreateSequenceAsync(_tenantId,
            new CreateSequenceRequest("MVP", OpportunityType.Rfp, AgencyType.StateLocal,
                new[]
                {
                    new CreateSequenceStepRequest(0, Guid.NewGuid(), "Re: A",
                        TimeSpan.Zero, TimeSpan.FromHours(23.99), 0)
                }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task CreateSequenceAsync_rejects_when_no_steps()
    {
        var svc = new OutreachSequenceService(new FakeOutreachRepository());
        var result = await svc.CreateSequenceAsync(_tenantId,
            new CreateSequenceRequest("Empty", OpportunityType.Rfp, AgencyType.StateLocal,
                Array.Empty<CreateSequenceStepRequest>()),
            CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("At least one step is required.");
    }

    [Fact]
    public async Task CreateSequenceAsync_rejects_inverted_send_window()
    {
        var repo = new FakeOutreachRepository();
        var template = OutreachTemplate.Create(_tenantId, "T1", "subj", "body");
        repo.Templates.Add(template);
        var svc = new OutreachSequenceService(repo);

        var result = await svc.CreateSequenceAsync(_tenantId,
            new CreateSequenceRequest("Bad", OpportunityType.Rfp, AgencyType.StateLocal,
                new[]
                {
                    new CreateSequenceStepRequest(0, template.Id, "subj",
                        TimeSpan.FromHours(22), TimeSpan.FromHours(14), 0)
                }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("send window end must be after start");
    }

    [Fact]
    public async Task ListSequencesAsync_returns_summary_with_step_count()
    {
        var repo = new FakeOutreachRepository();
        var template = OutreachTemplate.Create(_tenantId, "T1", "subj", "body");
        repo.Templates.Add(template);
        var seq = OutreachSequence.Create(_tenantId, "S1", OpportunityType.Rfp, AgencyType.FederalCivilian);
        seq.AddStep(0, template.Id, "subj", TimeSpan.Zero, TimeSpan.FromHours(23), 0);
        seq.AddStep(3, template.Id, "subj", TimeSpan.Zero, TimeSpan.FromHours(23), 0);
        repo.Sequences.Add(seq);

        var svc = new OutreachSequenceService(repo);
        var summaries = await svc.ListSequencesAsync(_tenantId, CancellationToken.None);

        summaries.Should().HaveCount(1);
        summaries[0].Name.Should().Be("S1");
        summaries[0].StepCount.Should().Be(2);
        summaries[0].AgencyType.Should().Be(AgencyType.FederalCivilian);
    }
}

internal class FakeOutreachRepository : IOutreachRepository
{
    public List<OutreachTemplate> Templates { get; } = new();
    public List<OutreachSequence> Sequences { get; } = new();
    public int SaveCount { get; private set; }

    public Task<OutreachTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Templates.FirstOrDefault(t => t.Id == id));
    public Task<IReadOnlyList<OutreachTemplate>> GetTemplatesAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OutreachTemplate>>(Templates);
    public Task AddTemplateAsync(OutreachTemplate template, CancellationToken ct)
    {
        Templates.Add(template);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutreachSequence>> GetSequencesAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OutreachSequence>>(Sequences);
    public Task<OutreachSequence?> GetSequenceByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Sequences.FirstOrDefault(s => s.Id == id));
    public Task AddSequenceAsync(OutreachSequence sequence, CancellationToken ct)
    {
        Sequences.Add(sequence);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<OutreachEnrollment?> GetEnrollmentByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OutreachEnrollment?>(null);
    public Task<OutreachEnrollment?> GetEnrollmentAsync(Guid t, Guid c, Guid o, CancellationToken ct) => Task.FromResult<OutreachEnrollment?>(null);
    public Task<IReadOnlyList<OutreachEnrollment>> GetDueEnrollmentsAsync(Guid t, DateTimeOffset a, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
    public Task<IReadOnlyList<OutreachEnrollment>> GetEnrollmentsByStatusAsync(Guid t, EnrollmentStatus s, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
    public Task AddEnrollmentAsync(OutreachEnrollment e, CancellationToken ct) => Task.CompletedTask;
    public Task<SequenceSnapshot?> GetSnapshotByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<SequenceSnapshot?>(null);
    public Task AddSnapshotAsync(SequenceSnapshot s, CancellationToken ct) => Task.CompletedTask;
    public Task<EmailActivity?> GetEmailByMessageIdAsync(Guid t, string m, CancellationToken ct) => Task.FromResult<EmailActivity?>(null);
    public Task<EmailActivity?> GetEmailBySubjectAndContactAsync(Guid t, string s, Guid c, CancellationToken ct) => Task.FromResult<EmailActivity?>(null);
    public Task AddEmailActivityAsync(EmailActivity a, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<Meridian.Application.Outreach.ReplyListItem>> GetRecentRepliesAsync(Guid t, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<Meridian.Application.Outreach.ReplyListItem>>(Array.Empty<Meridian.Application.Outreach.ReplyListItem>());
    public Task<bool> IsSuppressedAsync(Guid t, string e, CancellationToken ct) => Task.FromResult(false);
    public Task AddSuppressionAsync(SuppressionEntry e, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<OutreachEnrollment>> GetActiveEnrollmentsForContactAsync(Guid t, Guid c, CancellationToken ct) => Task.FromResult<IReadOnlyList<OutreachEnrollment>>(Array.Empty<OutreachEnrollment>());
}
