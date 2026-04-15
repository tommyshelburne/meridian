using Meridian.Application.Ports;
using Meridian.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public class ContactRepository : IContactRepository
{
    private readonly MeridianDbContext _db;

    public ContactRepository(MeridianDbContext db) => _db = db;

    public async Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Contact?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct)
        => await _db.Contacts.FirstOrDefaultAsync(c => c.Email != null && c.Email.ToLower() == email.ToLower(), ct);

    public async Task<IReadOnlyList<Contact>> GetByAgencyAsync(Guid tenantId, string agencyName, CancellationToken ct)
        => await _db.Contacts.Where(c => c.Agency.Name == agencyName).ToListAsync(ct);

    public async Task<IReadOnlyList<Contact>> GetUnenrichedForOpportunityAsync(Guid tenantId, CancellationToken ct)
        => await _db.Contacts.Where(c => c.Email == null && !c.IsOptedOut && !c.IsBounced).ToListAsync(ct);

    public async Task AddAsync(Contact contact, CancellationToken ct)
        => await _db.Contacts.AddAsync(contact, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
