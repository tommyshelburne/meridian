using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Outreach;

namespace Meridian.Unit.Domain;

public class OutreachEnrollmentTests
{
    [Fact]
    public void Create_starts_active_at_step_one()
    {
        var enrollment = OutreachEnrollment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));

        enrollment.Status.Should().Be(EnrollmentStatus.Active);
        enrollment.CurrentStep.Should().Be(1);
        enrollment.IsSendable.Should().BeTrue();
    }

    [Fact]
    public void AdvanceStep_completes_at_final_step()
    {
        var enrollment = OutreachEnrollment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        enrollment.AdvanceStep(DateTimeOffset.UtcNow.AddDays(3), totalSteps: 3);
        enrollment.CurrentStep.Should().Be(2);
        enrollment.Status.Should().Be(EnrollmentStatus.Active);

        enrollment.AdvanceStep(DateTimeOffset.UtcNow.AddDays(5), totalSteps: 3);
        enrollment.CurrentStep.Should().Be(3);

        enrollment.AdvanceStep(DateTimeOffset.UtcNow.AddDays(8), totalSteps: 3);
        enrollment.Status.Should().Be(EnrollmentStatus.Completed);
        enrollment.NextSendAt.Should().BeNull();
    }

    [Fact]
    public void MarkReplied_stops_sequence()
    {
        var enrollment = OutreachEnrollment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        enrollment.MarkReplied();

        enrollment.Status.Should().Be(EnrollmentStatus.Replied);
        enrollment.IsSendable.Should().BeFalse();
    }

    [Fact]
    public void Pause_and_resume_resets_next_send()
    {
        var enrollment = OutreachEnrollment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        enrollment.Pause("Phone call in progress");
        enrollment.Status.Should().Be(EnrollmentStatus.Paused);
        enrollment.NextSendAt.Should().BeNull();
        enrollment.PausedReason.Should().Be("Phone call in progress");

        var resumeNext = DateTimeOffset.UtcNow.AddDays(3);
        enrollment.Resume(resumeNext);
        enrollment.Status.Should().Be(EnrollmentStatus.Active);
        enrollment.NextSendAt.Should().Be(resumeNext);
        enrollment.PausedReason.Should().BeNull();
    }
}
