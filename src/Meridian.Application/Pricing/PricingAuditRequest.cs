namespace Meridian.Application.Pricing;

/// <summary>
/// Input to <see cref="PricingAuditService"/>. A pure value object — all market-sizing
/// figures (TAM, serviceable pipeline) are supplied by the caller; estimating them from
/// USASpending or other sources is out of scope for the pricing engine.
/// </summary>
public sealed record PricingAuditRequest(
    decimal TenantAnnualRevenue,
    int EmployeeCount,
    int BdTeamSize,
    SetAsideStatus SetAside,
    decimal AddressableTam,
    decimal ServiceablePipeline,
    int SourceCount,
    int MonthlySendVolume,
    int CrmConnections,
    IntegrationComplexity IntegrationComplexity,
    bool NeedsMultiStepSequences,
    ProcurementPursuitLevel PursuitLevel,
    bool HasAdaptableCrm,
    bool DoesOutreachAlready);
