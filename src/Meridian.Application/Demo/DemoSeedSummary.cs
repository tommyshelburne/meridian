namespace Meridian.Application.Demo;

// Rows added by a seed pass. All zeros means the tenant was already seeded.
public record DemoSeedSummary(
    int Opportunities,
    int Contacts,
    int Templates,
    int Sequences,
    int Enrollments,
    int EmailActivities);
