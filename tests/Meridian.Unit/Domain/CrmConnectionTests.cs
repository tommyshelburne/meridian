using FluentAssertions;
using Meridian.Domain.Common;
using Meridian.Domain.Crm;

namespace Meridian.Unit.Domain;

public class CrmConnectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_initializes_active_with_required_fields()
    {
        var connection = CrmConnection.Create(Tenant, CrmProvider.Pipedrive, "enc-token");

        connection.TenantId.Should().Be(Tenant);
        connection.Provider.Should().Be(CrmProvider.Pipedrive);
        connection.EncryptedAuthToken.Should().Be("enc-token");
        connection.IsActive.Should().BeTrue();
        connection.EncryptedRefreshToken.Should().BeNull();
        connection.ExpiresAt.Should().BeNull();
        connection.DefaultPipelineId.Should().BeNull();
        connection.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Create_rejects_None_provider()
    {
        FluentActions.Invoking(() => CrmConnection.Create(Tenant, CrmProvider.None, "enc-token"))
            .Should().Throw<ArgumentException>()
            .WithMessage("*None*");
    }

    [Fact]
    public void Create_rejects_empty_tenant()
    {
        FluentActions.Invoking(() => CrmConnection.Create(Guid.Empty, CrmProvider.Pipedrive, "enc-token"))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_auth_token()
    {
        FluentActions.Invoking(() => CrmConnection.Create(Tenant, CrmProvider.Pipedrive, ""))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RotateAuthToken_replaces_credentials_and_touches_updated_at()
    {
        var connection = CrmConnection.Create(Tenant, CrmProvider.Pipedrive, "old");
        var initial = connection.UpdatedAt;
        Thread.Sleep(5);

        connection.RotateAuthToken("new", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        connection.EncryptedAuthToken.Should().Be("new");
        connection.EncryptedRefreshToken.Should().Be("refresh");
        connection.ExpiresAt.Should().NotBeNull();
        connection.UpdatedAt.Should().BeAfter(initial);
    }

    [Fact]
    public void ChangeProvider_swaps_provider_and_clears_default_pipeline()
    {
        var connection = CrmConnection.Create(Tenant, CrmProvider.Pipedrive, "old", defaultPipelineId: "pipeline-1");

        connection.ChangeProvider(CrmProvider.HubSpot, "new-token");

        connection.Provider.Should().Be(CrmProvider.HubSpot);
        connection.EncryptedAuthToken.Should().Be("new-token");
        connection.DefaultPipelineId.Should().BeNull();
    }

    [Fact]
    public void ChangeProvider_rejects_None()
    {
        var connection = CrmConnection.Create(Tenant, CrmProvider.Pipedrive, "old");

        FluentActions.Invoking(() => connection.ChangeProvider(CrmProvider.None, "x"))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivate_then_Activate_round_trips_IsActive()
    {
        var connection = CrmConnection.Create(Tenant, CrmProvider.Pipedrive, "token");

        connection.Deactivate();
        connection.IsActive.Should().BeFalse();

        connection.Activate();
        connection.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_returns_true_only_when_ExpiresAt_in_past()
    {
        var connection = CrmConnection.Create(Tenant, CrmProvider.Pipedrive, "token",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        connection.IsExpired(DateTimeOffset.UtcNow).Should().BeTrue();

        connection.RotateAuthToken("token2", expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        connection.IsExpired(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_returns_false_when_ExpiresAt_null()
    {
        var connection = CrmConnection.Create(Tenant, CrmProvider.Pipedrive, "token");

        connection.IsExpired(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void SetDefaultPipelineId_normalizes_blank_to_null()
    {
        var connection = CrmConnection.Create(Tenant, CrmProvider.Pipedrive, "token");

        connection.SetDefaultPipelineId("  pipeline-9  ");
        connection.DefaultPipelineId.Should().Be("pipeline-9");

        connection.SetDefaultPipelineId("");
        connection.DefaultPipelineId.Should().BeNull();
    }
}
