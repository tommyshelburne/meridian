using Meridian.Domain.Common;
using Meridian.Domain.Scoring;

namespace Meridian.Domain.Opportunities;

public class Opportunity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public OpportunitySource Source { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public Agency Agency { get; private set; } = null!;
    public decimal? EstimatedValue { get; private set; }
    public int? EstimatedSeats { get; private set; }
    public SeatEstimateConfidence SeatConfidence { get; private set; }
    public DateTimeOffset PostedDate { get; private set; }
    public DateTimeOffset? ResponseDeadline { get; private set; }
    public string? NaicsCode { get; private set; }
    public ProcurementVehicle? ProcurementVehicle { get; private set; }
    public bool IsRecompete { get; private set; }
    public BidScore? Score { get; private set; }
    public OpportunityStatus Status { get; private set; }
    public DateTimeOffset? WatchedSince { get; private set; }
    public DateTimeOffset? LastAmendedAt { get; private set; }

    private readonly List<OpportunityContact> _contacts = new();
    public IReadOnlyCollection<OpportunityContact> Contacts => _contacts.AsReadOnly();

    private Opportunity() { }

    public static Opportunity Create(
        Guid tenantId,
        string externalId,
        OpportunitySource source,
        string title,
        string description,
        Agency agency,
        DateTimeOffset postedDate,
        DateTimeOffset? responseDeadline = null,
        string? naicsCode = null,
        decimal? estimatedValue = null,
        ProcurementVehicle? procurementVehicle = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Opportunity title is required.", nameof(title));

        return new Opportunity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalId = externalId,
            Source = source,
            Title = title,
            Description = description ?? string.Empty,
            Agency = agency,
            PostedDate = postedDate,
            ResponseDeadline = responseDeadline,
            NaicsCode = naicsCode,
            EstimatedValue = estimatedValue,
            ProcurementVehicle = procurementVehicle,
            SeatConfidence = SeatEstimateConfidence.Unknown,
            Status = OpportunityStatus.New
        };
    }

    public void ApplyScore(BidScore score)
    {
        Score = score ?? throw new ArgumentNullException(nameof(score));
        Status = score.Verdict switch
        {
            ScoreVerdict.Pursue => OpportunityStatus.Scored,
            ScoreVerdict.Partner => OpportunityStatus.Scored,
            ScoreVerdict.NoBid => OpportunityStatus.NoBid,
            _ => OpportunityStatus.Scored
        };
    }

    public void SetSeatEstimate(int seats, SeatEstimateConfidence confidence)
    {
        EstimatedSeats = seats;
        SeatConfidence = confidence;
    }

    public void MarkRecompete() => IsRecompete = true;

    public void Watch()
    {
        WatchedSince = DateTimeOffset.UtcNow;
        Status = OpportunityStatus.Watching;
    }

    public void Approve() => Status = OpportunityStatus.PendingReview;

    public void Pursue() => Status = OpportunityStatus.Pursuing;

    public void Partner() => Status = OpportunityStatus.Partnering;

    public void Reject() => Status = OpportunityStatus.Rejected;

    public void RecordAmendment(DateTimeOffset amendedAt) => LastAmendedAt = amendedAt;

    public void AddContact(OpportunityContact contact)
    {
        if (_contacts.Any(c => c.ContactId == contact.ContactId))
            return;
        _contacts.Add(contact);
    }
}
