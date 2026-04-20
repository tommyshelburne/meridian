using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Tenants;
using Meridian.Domain.Users;

namespace Meridian.Unit.Application.Auth;

public class MembershipServiceTests
{
    private static (MembershipService Service, AuthServiceTestFixture Fx, Guid TenantId, Guid OwnerId) Setup()
    {
        var fx = new AuthServiceTestFixture();
        var tenant = Tenant.Create("Acme", "acme");
        fx.Tenants.Items.Add(tenant);
        var owner = User.CreateWithPassword("owner@acme.test", "Owner", fx.PasswordHasher.Hash("correct-horse-battery"));
        owner.VerifyEmail();
        fx.Users.Items.Add(owner);
        fx.Memberships.Items.Add(UserTenant.CreateOwner(owner.Id, tenant.Id));
        var svc = new MembershipService(fx.Users, fx.Memberships, fx.AuthTokens,
            fx.PasswordHasher, fx.TokenHasher, fx.Emails,
            new AuthEmailOptions { BaseUrl = "https://portal.test" });
        return (svc, fx, tenant.Id, owner.Id);
    }

    [Fact]
    public async Task GetMembers_returns_active_members()
    {
        var (svc, _, tenantId, _) = Setup();

        var members = await svc.GetMembersAsync(tenantId, CancellationToken.None);

        members.Should().ContainSingle(m => m.Role == UserRole.Owner && m.Email == "owner@acme.test");
    }

    [Fact]
    public async Task Invite_new_user_creates_user_and_sends_reset_link()
    {
        var (svc, fx, tenantId, ownerId) = Setup();

        var result = await svc.InviteAsync(tenantId, "new@acme.test", "New Person",
            UserRole.Operator, ownerId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.Users.Items.Should().HaveCount(2);
        fx.Memberships.Items.Should().Contain(m =>
            m.TenantId == tenantId && m.Role == UserRole.Operator &&
            m.Status == MembershipStatus.Active);
        fx.AuthTokens.Resets.Should().HaveCount(1);
        fx.Emails.Sent.Should().Contain(e => e.Subject.Contains("invited") && e.BodyHtml.Contains("reset-password"));
    }

    [Fact]
    public async Task Invite_existing_user_creates_pending_membership()
    {
        var (svc, fx, tenantId, ownerId) = Setup();
        var other = User.CreateWithPassword("other@acme.test", "Other", fx.PasswordHasher.Hash("very-secure-password"));
        other.VerifyEmail();
        fx.Users.Items.Add(other);

        var result = await svc.InviteAsync(tenantId, "other@acme.test", "Other",
            UserRole.Admin, ownerId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.Users.Items.Should().HaveCount(2);
        fx.AuthTokens.Resets.Should().BeEmpty();
        fx.Memberships.Items.Should().Contain(m =>
            m.UserId == other.Id && m.TenantId == tenantId &&
            m.Status == MembershipStatus.Pending && m.Role == UserRole.Admin);
        fx.Emails.Sent.Should().Contain(e => e.Subject.Contains("invited"));
    }

    [Fact]
    public async Task Invite_rejects_existing_active_member()
    {
        var (svc, _, tenantId, ownerId) = Setup();

        var result = await svc.InviteAsync(tenantId, "owner@acme.test", "Owner",
            UserRole.Admin, ownerId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeRole_updates_role()
    {
        var (svc, fx, tenantId, ownerId) = Setup();
        await svc.InviteAsync(tenantId, "new@acme.test", "New", UserRole.Operator, ownerId, CancellationToken.None);
        var newUserId = fx.Users.Items.Single(u => u.Email == "new@acme.test").Id;

        var result = await svc.ChangeRoleAsync(tenantId, newUserId, UserRole.Admin, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.Memberships.Items.Single(m => m.UserId == newUserId).Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task Remove_marks_membership_removed()
    {
        var (svc, fx, tenantId, ownerId) = Setup();
        await svc.InviteAsync(tenantId, "new@acme.test", "New", UserRole.Operator, ownerId, CancellationToken.None);
        var newUserId = fx.Users.Items.Single(u => u.Email == "new@acme.test").Id;

        var result = await svc.RemoveAsync(tenantId, newUserId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.Memberships.Items.Single(m => m.UserId == newUserId).Status
            .Should().Be(MembershipStatus.Removed);
    }
}
