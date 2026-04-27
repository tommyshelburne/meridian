using Meridian.Application.Ports;
using Meridian.Domain.Common;

namespace Meridian.Infrastructure.Crm;

public class CrmAdapterFactory : ICrmAdapterFactory
{
    private readonly Dictionary<CrmProvider, ICrmAdapter> _adapters;

    public CrmAdapterFactory(IEnumerable<ICrmAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(a => a.Provider);
    }

    public ICrmAdapter Resolve(CrmProvider provider)
    {
        if (!_adapters.TryGetValue(provider, out var adapter))
            throw new InvalidOperationException(
                $"No adapter registered for {provider}. Register an ICrmAdapter with this Provider.");
        return adapter;
    }
}
