using FluentAssertions;
using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Sources;
using Meridian.Infrastructure.Ingestion;

namespace Meridian.Unit.Infrastructure;

public class SourceAdapterFactoryTests
{
    [Fact]
    public void Resolve_returns_adapter_matching_type()
    {
        var sam = new StubAdapter(SourceAdapterType.SamGov);
        var rss = new StubAdapter(SourceAdapterType.GenericRss);
        var factory = new SourceAdapterFactory(new IOpportunitySourceAdapter[] { sam, rss });

        factory.Resolve(SourceAdapterType.GenericRss).Should().BeSameAs(rss);
        factory.Resolve(SourceAdapterType.SamGov).Should().BeSameAs(sam);
    }

    [Fact]
    public void Resolve_throws_when_adapter_not_registered()
    {
        var factory = new SourceAdapterFactory(new IOpportunitySourceAdapter[]
        {
            new StubAdapter(SourceAdapterType.SamGov)
        });

        FluentActions.Invoking(() => factory.Resolve(SourceAdapterType.GenericRest))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*GenericRest*");
    }

    [Fact]
    public void Constructor_throws_on_duplicate_adapter_types()
    {
        FluentActions.Invoking(() => new SourceAdapterFactory(new IOpportunitySourceAdapter[]
        {
            new StubAdapter(SourceAdapterType.SamGov),
            new StubAdapter(SourceAdapterType.SamGov)
        })).Should().Throw<ArgumentException>();
    }

    private class StubAdapter : IOpportunitySourceAdapter
    {
        public SourceAdapterType AdapterType { get; }
        public StubAdapter(SourceAdapterType type) => AdapterType = type;

        public Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
            SourceDefinition source, CancellationToken ct)
            => Task.FromResult(ServiceResult<IReadOnlyList<IngestedOpportunity>>.Ok(Array.Empty<IngestedOpportunity>()));
    }
}
