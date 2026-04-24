using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Tenants;
using Meridian.Domain.Users;

namespace Meridian.Unit.Application.Auth;

public class AuthServiceOidcTests
{
    private readonly AuthServiceTestFixture _fx = new();

    [Fact]
    public async Task Auto_provisions_user_and_active_membership_on_first_signin()
    {
        var tenant = Tenant.Create("Acme", "acme");
        _fx.Tenants.Items.Add(tenant);
        var svc = _fx.BuildService();

        var result = await svc.SignInWithOidcAsync(
            tenant.Id, "alice@acme.com", "Alice Smith", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _fx.Users.Items.Should().ContainSingle(u => u.Email == "alice@acme.com");
        var created = _fx.Users.Items.Single();
        created.EmailVerified.Should().BeTrue("OIDC IdP verifies the email");
        created.PasswordHash.Should().BeNull();
        _fx.Memberships.Items.Should().ContainSingle();
        var membership = _fx.Memberships.Items.Single();
        membership.Status.Should().Be(MembershipStatus.Active);
        membership.Role.Should().Be(UserRole.Operator);
    }

    [Fact]
    public async Task Reuses_existing_user_when_email_already_known()
    {
        var tenant = Tenant.Create("Acme", "acme");
        _fx.Tenants.Items.Add(tenant);
        var existingUser = User.CreateOidcOnly("alice@acme.com", "Alice");
        _fx.Users.Items.Add(existingUser);
        var svc = _fx.BuildService();

        var result = await svc.SignInWithOidcAsync(
            tenant.Id, "alice@acme.com", "Alice Smith", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _fx.Users.Items.Should().HaveCount(1);
        result.UserId.Should().Be(existingUser.Id);
    }

    [Fact]
    public async Task Reuses_existing_active_membership()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var user = User.CreateOidcOnly("alice@acme.com", "Alice");
        var existingMembership = UserTenant.CreateActive(user.Id, tenant.Id, UserRole.Admin);
        _fx.Tenants.Items.Add(tenant);
        _fx.Users.Items.Add(user);
        _fx.Memberships.Items.Add(existingMembership);
        var svc = _fx.BuildService();

        var result = await svc.SignInWithOidcAsync(
            tenant.Id, "alice@acme.com", "Alice", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _fx.Memberships.Items.Should().ContainSingle();
        result.Memberships[0].Role.Should().Be(UserRole.Admin, "existing role is preserved");
    }

    [Fact]
    public async Task Auto_accepts_pending_invite_on_first_oidc_signin()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var user = User.CreateOidcOnly("alice@acme.com", "Alice");
        var inviter = User.CreateWithPassword("admin@acme.com", "Admin", "ignored");
        var pending = UserTenant.Invite(user.Id, tenant.Id, UserRole.Viewer, inviter.Id);
        _fx.Tenants.Items.Add(tenant);
        _fx.Users.Items.Add(user);
        _fx.Memberships.Items.Add(pending);
        var svc = _fx.BuildService();

        var result = await svc.SignInWithOidcAsync(
            tenant.Id, "alice@acme.com", "Alice", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _fx.Memberships.Items.Single().Status.Should().Be(MembershipStatus.Active);
    }

    [Fact]
    public async Task Rejects_when_membership_was_removed()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var user = User.CreateOidcOnly("alice@acme.com", "Alice");
        var membership = UserTenant.CreateActive(user.Id, tenant.Id, UserRole.Operator);
        membership.Remove();
        _fx.Tenants.Items.Add(tenant);
        _fx.Users.Items.Add(user);
        _fx.Memberships.Items.Add(membership);
        var svc = _fx.BuildService();

        var result = await svc.SignInWithOidcAsync(
            tenant.Id, "alice@acme.com", "Alice", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Outcome.Should().Be(LoginOutcome.NoActiveMembership);
    }

    [Fact]
    public async Task Rejects_when_tenant_does_not_exist()
    {
        var svc = _fx.BuildService();

        var result = await svc.SignInWithOidcAsync(
            Guid.NewGuid(), "alice@acme.com", "Alice", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Outcome.Should().Be(LoginOutcome.NoActiveMembership);
    }

    [Fact]
    public async Task Rejects_when_email_is_blank()
    {
        var tenant = Tenant.Create("Acme", "acme");
        _fx.Tenants.Items.Add(tenant);
        var svc = _fx.BuildService();

        var result = await svc.SignInWithOidcAsync(
            tenant.Id, "", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Outcome.Should().Be(LoginOutcome.InvalidCredentials);
    }

    [Fact]
    public async Task Falls_back_to_email_when_full_name_blank()
    {
        var tenant = Tenant.Create("Acme", "acme");
        _fx.Tenants.Items.Add(tenant);
        var svc = _fx.BuildService();

        await svc.SignInWithOidcAsync(tenant.Id, "alice@acme.com", null, CancellationToken.None);

        _fx.Users.Items.Single().FullName.Should().Be("alice@acme.com");
    }

    [Fact]
    public async Task Rejects_when_user_disabled()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var user = User.CreateOidcOnly("alice@acme.com", "Alice");
        user.Disable();
        _fx.Tenants.Items.Add(tenant);
        _fx.Users.Items.Add(user);
        var svc = _fx.BuildService();

        var result = await svc.SignInWithOidcAsync(
            tenant.Id, "alice@acme.com", "Alice", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Outcome.Should().Be(LoginOutcome.Disabled);
    }
}
