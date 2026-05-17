namespace Meridian.Application.Pricing;

/// <summary>
/// Output of <see cref="PricingAuditService"/>: a deterministic pricing recommendation
/// for a prospective tenant, including the recommended tier, ACV bracket, onboarding fee,
/// the computed ICP score, the pursue/decline decision, and a human-readable rationale.
/// </summary>
public sealed record PricingRecommendation(
    PricingTier RecommendedTier,
    decimal TargetAcv,
    decimal FloorAcv,
    decimal CeilingAcv,
    decimal OnboardingFee,
    int IcpScore,
    PricingDecision Decision,
    IReadOnlyList<string> Rationale);
