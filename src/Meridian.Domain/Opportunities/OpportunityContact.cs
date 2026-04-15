namespace Meridian.Domain.Opportunities;

public class OpportunityContact
{
    public Guid OpportunityId { get; private set; }
    public Guid ContactId { get; private set; }
    public bool IsPrimary { get; private set; }

    private OpportunityContact() { }

    public static OpportunityContact Create(Guid opportunityId, Guid contactId, bool isPrimary = false)
    {
        return new OpportunityContact
        {
            OpportunityId = opportunityId,
            ContactId = contactId,
            IsPrimary = isPrimary
        };
    }
}
