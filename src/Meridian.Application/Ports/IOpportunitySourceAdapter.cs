using Meridian.Application.Common;
using Meridian.Domain.Sources;

namespace Meridian.Application.Ports;

public interface IOpportunitySourceAdapter
{
    SourceAdapterType AdapterType { get; }

    Task<ServiceResult<IReadOnlyList<IngestedOpportunity>>> FetchAsync(
        SourceDefinition source, CancellationToken ct);
}
