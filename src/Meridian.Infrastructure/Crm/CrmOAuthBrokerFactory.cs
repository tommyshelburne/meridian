using Meridian.Application.Ports;
using Meridian.Domain.Common;

namespace Meridian.Infrastructure.Crm;

public class CrmOAuthBrokerFactory : ICrmOAuthBrokerFactory
{
    private readonly Dictionary<CrmProvider, ICrmOAuthBroker> _brokers;

    public CrmOAuthBrokerFactory(IEnumerable<ICrmOAuthBroker> brokers)
    {
        _brokers = brokers.ToDictionary(b => b.Provider);
    }

    public ICrmOAuthBroker Resolve(CrmProvider provider)
    {
        if (!_brokers.TryGetValue(provider, out var broker))
            throw new InvalidOperationException(
                $"No OAuth broker registered for {provider}. Register an ICrmOAuthBroker with this Provider.");
        return broker;
    }

    public bool TryResolve(CrmProvider provider, out ICrmOAuthBroker broker)
    {
        if (_brokers.TryGetValue(provider, out var found))
        {
            broker = found;
            return true;
        }
        broker = default!;
        return false;
    }
}
