namespace Meridian.Application.Demo;

public record DemoProvisionResult(
    Guid TenantId,
    Guid UserId,
    string Slug,
    bool AlreadyExisted,
    DemoSeedSummary Seeded);
