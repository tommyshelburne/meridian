namespace Meridian.Application.Pricing;

/// <summary>
/// PLACEHOLDER VALUES — UNCALIBRATED. These must be calibrated against real closed-deal
/// data before production use. See pricing decisions record (Memento entry 93d38c72) and
/// algorithm spec (2b59fd2f).
/// </summary>
public static class PricingConstants
{
    // --- Tier base prices (annual contract value anchor) ---
    public const decimal StarterBasePrice = 9_000m;
    public const decimal PursuitBasePrice = 36_000m;
    public const decimal EnterpriseBasePrice = 72_000m;

    // --- Tier bands [floor, ceiling] ---
    public const decimal StarterFloor = 6_000m;
    public const decimal StarterCeiling = 12_000m;
    public const decimal PursuitFloor = 24_000m;
    public const decimal PursuitCeiling = 48_000m;
    public const decimal EnterpriseFloor = 60_000m;
    public const decimal EnterpriseCeiling = 150_000m;

    // --- Value anchoring ---
    /// <summary>Fraction of serviceable pipeline assumed to convert into captured value.</summary>
    public const decimal ValueCaptureRate = 0.04m;
    /// <summary>Fraction of captured value the tenant should pay (value-based ceiling).</summary>
    public const decimal ValueAnchorRate = 0.15m;

    // --- Onboarding fee components ---
    public const decimal OnboardingBaseFee = 5_000m;
    public const decimal OnboardingCustomAdaptersFee = 8_000m;
    public const decimal OnboardingSsoFee = 5_000m;
    public const decimal OnboardingIsolatedInfraFee = 12_000m;
    /// <summary>Added per CRM connection beyond the first.</summary>
    public const decimal OnboardingPerExtraCrmFee = 2_000m;

    // --- ICP multiplier thresholds ---
    public const int IcpPursueThreshold = 40;
    public const int IcpFullConfidenceThreshold = 60;
    public const decimal IcpFullConfidenceMultiplier = 1.00m;
    public const decimal IcpServicesAttachMultiplier = 0.85m;

    // --- Size multiplier (by tenant annual revenue) ---
    public const decimal SizeMultiplierLargeThreshold = 25_000_000m;
    public const decimal SizeMultiplierMidThreshold = 5_000_000m;
    public const decimal SizeMultiplierLarge = 1.10m;
    public const decimal SizeMultiplierMid = 1.00m;
    public const decimal SizeMultiplierSmall = 0.90m;

    // --- TAM multiplier (by addressable TAM) ---
    public const decimal TamMultiplierVeryHighThreshold = 100_000_000m;
    public const decimal TamMultiplierHighThreshold = 25_000_000m;
    public const decimal TamMultiplierMidThreshold = 5_000_000m;
    public const decimal TamMultiplierVeryHigh = 1.15m;
    public const decimal TamMultiplierHigh = 1.05m;
    public const decimal TamMultiplierMid = 1.00m;
    public const decimal TamMultiplierLow = 0.85m;

    // --- Tier-selection thresholds ---
    /// <summary>Monthly send volume above which Enterprise tier is forced.</summary>
    public const int EnterpriseVeryHighSendThreshold = 50_000;
    /// <summary>Monthly send volume above which Pursuit tier is indicated.</summary>
    public const int PursuitSendThreshold = 500;

    // --- Floor-relative-to-target guardrail ---
    public const decimal FloorAcvFractionOfTarget = 0.85m;
}
