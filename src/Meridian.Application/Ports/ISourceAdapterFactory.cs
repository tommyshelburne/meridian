using Meridian.Domain.Sources;

namespace Meridian.Application.Ports;

public interface ISourceAdapterFactory
{
    IOpportunitySourceAdapter Resolve(SourceAdapterType adapterType);
}
