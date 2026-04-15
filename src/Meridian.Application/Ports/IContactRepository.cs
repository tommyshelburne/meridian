using Meridian.Domain.Contacts;

namespace Meridian.Application.Ports;

public interface IContactRepository
{
    Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Contact?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct);
    Task<IReadOnlyList<Contact>> GetByAgencyAsync(Guid tenantId, string agencyName, CancellationToken ct);
    Task<IReadOnlyList<Contact>> GetUnenrichedForOpportunityAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(Contact contact, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
