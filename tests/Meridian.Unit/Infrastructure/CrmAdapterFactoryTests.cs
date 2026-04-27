using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Crm;

namespace Meridian.Unit.Infrastructure;

public class CrmAdapterFactoryTests
{
    [Fact]
    public void Resolve_returns_adapter_matching_provider()
    {
        var noop = new StubCrmAdapter(CrmProvider.None);
        var pipedrive = new StubCrmAdapter(CrmProvider.Pipedrive);
        var factory = new CrmAdapterFactory(new ICrmAdapter[] { noop, pipedrive });

        factory.Resolve(CrmProvider.Pipedrive).Should().BeSameAs(pipedrive);
        factory.Resolve(CrmProvider.None).Should().BeSameAs(noop);
    }

    [Fact]
    public void Resolve_throws_when_provider_not_registered()
    {
        var factory = new CrmAdapterFactory(new ICrmAdapter[]
        {
            new StubCrmAdapter(CrmProvider.None)
        });

        FluentActions.Invoking(() => factory.Resolve(CrmProvider.HubSpot))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*HubSpot*");
    }

    [Fact]
    public void Constructor_throws_on_duplicate_providers()
    {
        FluentActions.Invoking(() => new CrmAdapterFactory(new ICrmAdapter[]
        {
            new StubCrmAdapter(CrmProvider.Pipedrive),
            new StubCrmAdapter(CrmProvider.Pipedrive)
        })).Should().Throw<ArgumentException>();
    }

    private class StubCrmAdapter : ICrmAdapter
    {
        public CrmProvider Provider { get; }
        public StubCrmAdapter(CrmProvider provider) => Provider = provider;

        public Task<ServiceResult<string>> FindOrCreateOrganizationAsync(
            CrmConnectionContext ctx, string agencyName, CancellationToken ct)
            => Task.FromResult(ServiceResult<string>.Ok("org"));

        public Task<ServiceResult<string>> CreateDealAsync(
            CrmConnectionContext ctx, Opportunity opportunity, string organizationId, CancellationToken ct)
            => Task.FromResult(ServiceResult<string>.Ok("deal"));

        public Task<ServiceResult> UpdateDealStageAsync(
            CrmConnectionContext ctx, string dealId, string stage, CancellationToken ct)
            => Task.FromResult(ServiceResult.Ok());

        public Task<ServiceResult> AddActivityAsync(
            CrmConnectionContext ctx, string dealId, string type, string description, CancellationToken ct)
            => Task.FromResult(ServiceResult.Ok());
    }
}
