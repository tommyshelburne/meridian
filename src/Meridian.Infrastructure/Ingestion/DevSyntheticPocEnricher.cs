using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;

namespace Meridian.Infrastructure.Ingestion;

// Dev-only enricher used by the soft-launch dry-run flow. The real enrichers
// (SAM.gov, USASpending) need API keys that aren't present in the local env,
// so without this the pipeline would never produce a contact and enrollment
// would skip every opportunity. Registered only in Development.
public class DevSyntheticPocEnricher : IPocEnricher
{
    public string SourceName => "dev-synthetic";

    public Task<ServiceResult<IReadOnlyList<Contact>>> EnrichAsync(
        Opportunity opportunity, Guid tenantId, CancellationToken ct)
    {
        var contact = Contact.Create(
            tenantId,
            fullName: "Jordan Buyer",
            agency: opportunity.Agency,
            source: ContactSource.Manual,
            confidenceScore: 0.85f,
            title: "Procurement Officer",
            email: $"jordan+{opportunity.Id:N}@example.test");
        return Task.FromResult(ServiceResult<IReadOnlyList<Contact>>.Ok(
            (IReadOnlyList<Contact>)new[] { contact }));
    }
}
