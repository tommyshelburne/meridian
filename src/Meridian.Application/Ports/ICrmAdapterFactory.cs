using Meridian.Domain.Common;

namespace Meridian.Application.Ports;

public interface ICrmAdapterFactory
{
    ICrmAdapter Resolve(CrmProvider provider);
}
