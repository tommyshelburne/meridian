using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Tenants;
using Meridian.Domain.Users;

namespace Meridian.Integration;

public class RolePermissionTests
{
    [Fact]
    public async Task Invite_and_change_role_persists_across_db_contexts()
    {
        using var fx = new IntegrationTestFixture();

        var (tenantId, ownerId) = await SeedOwner(fx);

        // Invite a new operator.
        await using (var db = fx.NewDbContext())
        {
            var svc = fx.BuildMembershipService(db);
            var invited = await svc.InviteAsync(tenantId, "ops@acme.test", "Ops Person",
                UserRole.Operator, ownerId, CancellationToken.None);
            invited.IsSuccess.Should().BeTrue();
        }

        // Verify state was persisted.
        Guid operatorId;
        await using (var db = fx.NewDbContext())
        {
            var svc = fx.BuildMembershipService(db);
            var members = await svc.GetMembersAsync(tenantId, CancellationToken.None);
            members.Should().HaveCount(2);
            var op = members.Single(m => m.Email == "ops@acme.test");
            op.Role.Should().Be(UserRole.Operator);
            op.Status.Should().Be(MembershipStatus.Active);
            operatorId = op.UserId;
        }

        // Promote Operator to Admin.
        await using (var db = fx.NewDbContext())
        {
            var svc = fx.BuildMembershipService(db);
            (await svc.ChangeRoleAsync(tenantId, operatorId, UserRole.Admin,
                CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        await using (var db = fx.NewDbContext())
        {
            var svc = fx.BuildMembershipService(db);
            var members = await svc.GetMembersAsync(tenantId, CancellationToken.None);
            members.Single(m => m.UserId == operatorId).Role.Should().Be(UserRole.Admin);
        }
    }

    [Fact]
    public async Task Remove_excludes_member_from_active_list()
    {
        using var fx = new IntegrationTestFixture();
        var (tenantId, ownerId) = await SeedOwner(fx);

        Guid targetId;
        await using (var db = fx.NewDbContext())
        {
            var svc = fx.BuildMembershipService(db);
            (await svc.InviteAsync(tenantId, "viewer@acme.test", "Viewer Person",
                UserRole.Viewer, ownerId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        await using (var db = fx.NewDbContext())
        {
            var svc = fx.BuildMembershipService(db);
            var members = await svc.GetMembersAsync(tenantId, CancellationToken.None);
            targetId = members.Single(m => m.Email == "viewer@acme.test").UserId;
            (await svc.RemoveAsync(tenantId, targetId, CancellationToken.None))
                .IsSuccess.Should().BeTrue();
        }

        await using (var db = fx.NewDbContext())
        {
            var svc = fx.BuildMembershipService(db);
            var members = await svc.GetMembersAsync(tenantId, CancellationToken.None);
            members.Should().NotContain(m => m.UserId == targetId);
        }
    }

    [Fact]
    public async Task Login_excludes_removed_membership()
    {
        using var fx = new IntegrationTestFixture();
        var (tenantId, ownerId) = await SeedOwner(fx);

        await using (var db = fx.NewDbContext())
        {
            var svc = fx.BuildMembershipService(db);
            (await svc.InviteAsync(tenantId, "temp@acme.test", "Temp Person",
                UserRole.Operator, ownerId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        // New user received an invite token via email — use it to set their password.
        var inviteToken = ExtractToken(fx.Emails.Sent.Last(e => e.To == "temp@acme.test").BodyHtml);
        await using (var db = fx.NewDbContext())
        {
            var auth = fx.BuildAuthService(db);
            (await auth.ResetPasswordAsync(inviteToken, "temp-user-password-123",
                CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        // User can log in with one active membership.
        await using (var db = fx.NewDbContext())
        {
            var auth = fx.BuildAuthService(db);
            var login = await auth.LoginAsync(new LoginRequest(
                "temp@acme.test", "temp-user-password-123", null), CancellationToken.None);
            login.Outcome.Should().Be(LoginOutcome.Success);
            login.Memberships.Should().ContainSingle(m => m.TenantId == tenantId);
        }

        // Remove the membership.
        Guid tempId;
        await using (var db = fx.NewDbContext())
        {
            var mem = fx.BuildMembershipService(db);
            var members = await mem.GetMembersAsync(tenantId, CancellationToken.None);
            tempId = members.Single(m => m.Email == "temp@acme.test").UserId;
            (await mem.RemoveAsync(tenantId, tempId, CancellationToken.None))
                .IsSuccess.Should().BeTrue();
        }

        await using (var db = fx.NewDbContext())
        {
            var auth = fx.BuildAuthService(db);
            var login = await auth.LoginAsync(new LoginRequest(
                "temp@acme.test", "temp-user-password-123", null), CancellationToken.None);
            login.Outcome.Should().Be(LoginOutcome.NoActiveMembership);
        }
    }

    private static async Task<(Guid TenantId, Guid OwnerId)> SeedOwner(IntegrationTestFixture fx)
    {
        var tenant = Tenant.Create("Acme", "acme");
        var owner = User.CreateWithPassword("owner@acme.test", "Owner Person",
            fx.Passwords.Hash("correct-horse-battery-staple"));
        owner.VerifyEmail();
        var membership = UserTenant.CreateOwner(owner.Id, tenant.Id);

        await using var db = fx.NewDbContext();
        db.Tenants.Add(tenant);
        db.Users.Add(owner);
        db.UserTenants.Add(membership);
        await db.SaveChangesAsync();
        return (tenant.Id, owner.Id);
    }

    private static string ExtractToken(string html)
    {
        var idx = html.IndexOf("token=", StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("Token not found in email body.");
        var start = idx + "token=".Length;
        var end = html.IndexOfAny(new[] { '"', '&', '<' }, start);
        return Uri.UnescapeDataString(html.Substring(start, end - start));
    }
}
