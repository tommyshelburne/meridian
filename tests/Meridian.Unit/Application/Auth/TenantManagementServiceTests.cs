using FluentAssertions;
using Meridian.Application.Auth;
using Meridian.Domain.Tenants;

namespace Meridian.Unit.Application.Auth;

public class TenantManagementServiceTests
{
    private static (TenantManagementService Service, AuthServiceTestFixture Fx, Guid TenantId) Setup()
    {
        var fx = new AuthServiceTestFixture();
        var tenant = Tenant.Create("Acme", "acme");
        fx.Tenants.Items.Add(tenant);
        return (new TenantManagementService(fx.Tenants), fx, tenant.Id);
    }

    [Fact]
    public async Task Rename_updates_name()
    {
        var (svc, fx, tenantId) = Setup();

        var result = await svc.RenameTenantAsync(tenantId, "Acme Corp", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fx.Tenants.Items.Single().Name.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task Rename_rejects_empty_name()
    {
        var (svc, _, tenantId) = Setup();

        var result = await svc.RenameTenantAsync(tenantId, "   ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Rename_fails_when_tenant_missing()
    {
        var (svc, _, _) = Setup();

        var result = await svc.RenameTenantAsync(Guid.NewGuid(), "New Name", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Get_returns_tenant()
    {
        var (svc, _, tenantId) = Setup();

        var tenant = await svc.GetAsync(tenantId, CancellationToken.None);

        tenant.Should().NotBeNull();
        tenant!.Slug.Should().Be("acme");
    }
}
