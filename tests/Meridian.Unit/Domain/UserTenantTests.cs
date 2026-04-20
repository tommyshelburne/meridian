using FluentAssertions;
using Meridian.Domain.Users;

namespace Meridian.Unit.Domain;

public class UserTenantTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InviterId = Guid.NewGuid();

    [Fact]
    public void CreateOwner_is_active_immediately()
    {
        var m = UserTenant.CreateOwner(UserId, TenantId);

        m.Role.Should().Be(UserRole.Owner);
        m.Status.Should().Be(MembershipStatus.Active);
        m.AcceptedAt.Should().NotBeNull();
        m.InvitedByUserId.Should().BeNull();
    }

    [Fact]
    public void Invite_creates_pending_membership()
    {
        var m = UserTenant.Invite(UserId, TenantId, UserRole.Operator, InviterId);

        m.Role.Should().Be(UserRole.Operator);
        m.Status.Should().Be(MembershipStatus.Pending);
        m.InvitedByUserId.Should().Be(InviterId);
        m.AcceptedAt.Should().BeNull();
    }

    [Fact]
    public void Accept_activates_pending_membership()
    {
        var m = UserTenant.Invite(UserId, TenantId, UserRole.Operator, InviterId);

        m.Accept();

        m.Status.Should().Be(MembershipStatus.Active);
        m.AcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public void Accept_on_active_throws()
    {
        var m = UserTenant.CreateOwner(UserId, TenantId);

        var act = () => m.Accept();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Remove_marks_membership_removed()
    {
        var m = UserTenant.CreateOwner(UserId, TenantId);

        m.Remove();

        m.Status.Should().Be(MembershipStatus.Removed);
        m.RemovedAt.Should().NotBeNull();
    }

    [Fact]
    public void ChangeRole_on_removed_throws()
    {
        var m = UserTenant.CreateOwner(UserId, TenantId);
        m.Remove();

        var act = () => m.ChangeRole(UserRole.Viewer);

        act.Should().Throw<InvalidOperationException>();
    }
}
