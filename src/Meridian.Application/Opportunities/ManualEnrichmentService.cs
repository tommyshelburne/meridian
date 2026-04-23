using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Contacts;
using Meridian.Domain.Opportunities;

namespace Meridian.Application.Opportunities;

public class ManualEnrichmentService
{
    private readonly IOpportunityRepository _opportunities;
    private readonly IContactRepository _contacts;

    public ManualEnrichmentService(IOpportunityRepository opportunities, IContactRepository contacts)
    {
        _opportunities = opportunities;
        _contacts = contacts;
    }

    public Task<IReadOnlyList<Opportunity>> GetUnenrichedAsync(Guid tenantId, CancellationToken ct)
        => _opportunities.GetUnenrichedAsync(tenantId, ct);

    public async Task<ServiceResult> AddContactAsync(
        Guid tenantId, Guid opportunityId, string fullName, string email, string? title, CancellationToken ct)
    {
        var opp = await _opportunities.GetByIdAsync(opportunityId, ct);
        if (opp is null || opp.TenantId != tenantId)
            return ServiceResult.Fail("Opportunity not found.");

        if (string.IsNullOrWhiteSpace(fullName))
            return ServiceResult.Fail("Contact name is required.");
        if (string.IsNullOrWhiteSpace(email))
            return ServiceResult.Fail("Contact email is required.");

        var trimmedEmail = email.Trim().ToLowerInvariant();

        var existing = await _contacts.GetByEmailAsync(tenantId, trimmedEmail, ct);
        Contact contact;
        if (existing is not null)
        {
            contact = existing;
        }
        else
        {
            try
            {
                contact = Contact.Create(
                    tenantId, fullName.Trim(), opp.Agency,
                    ContactSource.Manual,
                    confidenceScore: 1.0f,
                    title: string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                    email: trimmedEmail);
            }
            catch (ArgumentException ex)
            {
                return ServiceResult.Fail(ex.Message);
            }

            await _contacts.AddAsync(contact, ct);
        }

        opp.AddContact(OpportunityContact.Create(opp.Id, contact.Id));

        await _contacts.SaveChangesAsync(ct);
        await _opportunities.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }
}
