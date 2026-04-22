using Meridian.Application.Ports;
using Meridian.Domain.Sources;

namespace Meridian.Infrastructure.Ingestion;

public class SourceAdapterFactory : ISourceAdapterFactory
{
    private readonly Dictionary<SourceAdapterType, IOpportunitySourceAdapter> _adapters;

    public SourceAdapterFactory(IEnumerable<IOpportunitySourceAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(a => a.AdapterType);
    }

    public IOpportunitySourceAdapter Resolve(SourceAdapterType adapterType)
    {
        if (!_adapters.TryGetValue(adapterType, out var adapter))
            throw new InvalidOperationException(
                $"No adapter registered for {adapterType}. Register an IOpportunitySourceAdapter with this AdapterType.");
        return adapter;
    }
}
