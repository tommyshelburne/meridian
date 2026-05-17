using System.Globalization;
using Meridian.Application.Common;

namespace Meridian.Application.Pricing;

/// <summary>
/// Deterministic pricing-recommendation engine for prospective Meridian tenants.
///
/// This service is a PURE, in-memory computation: no I/O, no database, no network.
/// All market-sizing inputs (TAM, serviceable pipeline) are supplied on the request.
/// Because it performs zero asynchronous work, the entry point <see cref="Audit"/> is
/// intentionally synchronous.
/// </summary>
public sealed class PricingAuditService
{
    /// <summary>
    /// Produces a pricing recommendation for the supplied prospect profile.
    /// Expected error paths (declines) are returned as a successful <see cref="ServiceResult{T}"/>
    /// carrying a <see cref="PricingDecision.Decline"/> recommendation; <see cref="ServiceResult{T}.Fail"/>
    /// is reserved for genuinely invalid input.
    /// </summary>
    public ServiceResult<PricingRecommendation> Audit(PricingAuditRequest request)
    {
        if (request is null)
        {
            return ServiceResult<PricingRecommendation>.Fail("Pricing audit request must not be null.");
        }

        // STEP 1 — ICP score (0-100).
        var icpScore = ScoreIcp(request, out var icpBreakdown);

        // STEP 2 — ICP gate.
        if (icpScore < PricingConstants.IcpPursueThreshold)
        {
            var declineRationale = new List<string>
            {
                $"ICP score is {icpScore} ({icpBreakdown}).",
                $"Score is below the pursue threshold of {PricingConstants.IcpPursueThreshold}; this prospect is not a fit for Meridian at this time.",
                "Decision: Decline."
            };
            return ServiceResult<PricingRecommendation>.Ok(new PricingRecommendation(
                RecommendedTier: PricingTier.Starter,
                TargetAcv: 0m,
                FloorAcv: 0m,
                CeilingAcv: 0m,
                OnboardingFee: 0m,
                IcpScore: icpScore,
                Decision: PricingDecision.Decline,
                Rationale: declineRationale));
        }

        // STEP 3 — Tier selection (Enterprise checked first).
        var (tier, tierReason) = SelectTier(request);

        // STEP 4 — Value ceiling derived from serviceable pipeline.
        var capturedValue = request.ServiceablePipeline * PricingConstants.ValueCaptureRate;
        var valueCeiling = capturedValue * PricingConstants.ValueAnchorRate;

        // STEP 5 — Affordability check: downgrade until the tier's base price fits the
        // value ceiling, or decline if even Starter cannot be justified.
        var downgradeNotes = new List<string>();
        while (TierBasePrice(tier) > valueCeiling && tier != PricingTier.Starter)
        {
            var previous = tier;
            tier = Downgrade(tier);
            downgradeNotes.Add(
                $"Downgraded {previous} -> {tier}: {previous} base price {Money(TierBasePrice(previous))} " +
                $"exceeds the value ceiling {Money(valueCeiling)}.");
        }

        if (TierBasePrice(tier) > valueCeiling)
        {
            var declineRationale = new List<string>
            {
                $"ICP score is {icpScore} ({icpBreakdown}) — above the pursue threshold.",
                $"Value ceiling is {Money(valueCeiling)} (serviceable pipeline {Money(request.ServiceablePipeline)} " +
                $"x {PricingConstants.ValueCaptureRate:P0} capture x {PricingConstants.ValueAnchorRate:P0} anchor).",
                $"This is below the Starter base price of {Money(PricingConstants.StarterBasePrice)}; " +
                "the prospect's serviceable pipeline cannot justify even the entry tier.",
                "Decision: Decline."
            };
            return ServiceResult<PricingRecommendation>.Ok(new PricingRecommendation(
                RecommendedTier: PricingTier.Starter,
                TargetAcv: 0m,
                FloorAcv: 0m,
                CeilingAcv: 0m,
                OnboardingFee: 0m,
                IcpScore: icpScore,
                Decision: PricingDecision.Decline,
                Rationale: declineRationale));
        }

        // STEP 6 — Multipliers.
        var icpMultiplier = icpScore >= PricingConstants.IcpFullConfidenceThreshold
            ? PricingConstants.IcpFullConfidenceMultiplier
            : PricingConstants.IcpServicesAttachMultiplier;
        var sizeMultiplier = SizeMultiplier(request.TenantAnnualRevenue);
        var tamMultiplier = TamMultiplier(request.AddressableTam);

        // STEP 7 — Output.
        var tierBase = TierBasePrice(tier);
        var tierFloor = TierFloor(tier);
        var tierCeiling = TierCeiling(tier);

        var rawAcv = tierBase * icpMultiplier * sizeMultiplier * tamMultiplier;
        var ceiling = Math.Min(tierCeiling, valueCeiling);
        var targetAcv = Math.Clamp(rawAcv, tierFloor, ceiling);
        var floorAcv = Math.Max(tierFloor, PricingConstants.FloorAcvFractionOfTarget * targetAcv);
        var onboardingFee = ComputeOnboardingFee(request);

        var decision = icpScore < PricingConstants.IcpFullConfidenceThreshold
            ? PricingDecision.PursueWithServicesAttach
            : PricingDecision.Pursue;

        var targetAcvRounded = RoundMoney(targetAcv);
        var floorAcvRounded = RoundMoney(floorAcv);
        var ceilingAcvRounded = RoundMoney(ceiling);
        var onboardingFeeRounded = RoundMoney(onboardingFee);

        var rationale = new List<string>
        {
            $"ICP score is {icpScore} ({icpBreakdown}) — above the pursue threshold of {PricingConstants.IcpPursueThreshold}.",
            $"Selected {tier} tier: {tierReason}.",
            $"Value ceiling is {Money(valueCeiling)} (serviceable pipeline {Money(request.ServiceablePipeline)} " +
            $"x {PricingConstants.ValueCaptureRate:P0} capture x {PricingConstants.ValueAnchorRate:P0} anchor)."
        };
        if (downgradeNotes.Count > 0)
        {
            rationale.AddRange(downgradeNotes);
        }
        rationale.Add(
            $"Multipliers applied: ICP {icpMultiplier:0.00}, size {sizeMultiplier:0.00}, TAM {tamMultiplier:0.00} " +
            $"on a {Money(tierBase)} base.");
        rationale.Add(
            $"Final ACV bracket: target {Money(targetAcvRounded)}, floor {Money(floorAcvRounded)}, " +
            $"ceiling {Money(ceilingAcvRounded)}; onboarding fee {Money(onboardingFeeRounded)}. " +
            $"Decision: {decision}.");

        return ServiceResult<PricingRecommendation>.Ok(new PricingRecommendation(
            RecommendedTier: tier,
            TargetAcv: targetAcvRounded,
            FloorAcv: floorAcvRounded,
            CeilingAcv: ceilingAcvRounded,
            OnboardingFee: onboardingFeeRounded,
            IcpScore: icpScore,
            Decision: decision,
            Rationale: rationale));
    }

    private static int ScoreIcp(PricingAuditRequest request, out string breakdown)
    {
        // Revenue band.
        var revenue = request.TenantAnnualRevenue;
        var revenueScore = revenue is >= 1_000_000m and <= 50_000_000m
            ? 25
            : revenue > 50_000_000m && revenue <= 250_000_000m
                ? 15
                : 5;

        // Pursuit level.
        var pursuitScore = request.PursuitLevel switch
        {
            ProcurementPursuitLevel.Regularly => 20,
            ProcurementPursuitLevel.Occasionally => 10,
            _ => 0
        };

        // TAM density (guard divide-by-zero).
        var ratio = revenue <= 0m ? 0m : request.AddressableTam / revenue;
        var tamDensityScore = ratio >= 10m ? 20 : ratio >= 3m ? 12 : 5;

        // Stack fit.
        var stackFitScore = request.HasAdaptableCrm ? 15 : 0;

        // Outreach motion.
        var outreachScore = request.DoesOutreachAlready ? 10 : 0;

        // Set-aside leverage.
        var setAsideScore = request.SetAside != SetAsideStatus.None ? 10 : 0;

        var total = revenueScore + pursuitScore + tamDensityScore
            + stackFitScore + outreachScore + setAsideScore;

        breakdown =
            $"revenue {revenueScore}, pursuit {pursuitScore}, TAM density {tamDensityScore}, " +
            $"stack fit {stackFitScore}, outreach {outreachScore}, set-aside {setAsideScore}";

        return total;
    }

    private static (PricingTier Tier, string Reason) SelectTier(PricingAuditRequest request)
    {
        if (request.IntegrationComplexity != IntegrationComplexity.None
            || request.MonthlySendVolume > PricingConstants.EnterpriseVeryHighSendThreshold)
        {
            var reasons = new List<string>();
            if (request.IntegrationComplexity != IntegrationComplexity.None)
            {
                reasons.Add($"integration complexity ({request.IntegrationComplexity})");
            }
            if (request.MonthlySendVolume > PricingConstants.EnterpriseVeryHighSendThreshold)
            {
                reasons.Add($"send volume {request.MonthlySendVolume:N0}/mo " +
                    $"exceeds {PricingConstants.EnterpriseVeryHighSendThreshold:N0}");
            }
            return (PricingTier.Enterprise, string.Join(" and ", reasons));
        }

        if (request.SourceCount >= 2
            || request.NeedsMultiStepSequences
            || request.MonthlySendVolume > PricingConstants.PursuitSendThreshold
            || request.CrmConnections >= 2)
        {
            var reasons = new List<string>();
            if (request.SourceCount >= 2)
            {
                reasons.Add($"{request.SourceCount} opportunity sources");
            }
            if (request.NeedsMultiStepSequences)
            {
                reasons.Add("multi-step sequences required");
            }
            if (request.MonthlySendVolume > PricingConstants.PursuitSendThreshold)
            {
                reasons.Add($"send volume {request.MonthlySendVolume:N0}/mo " +
                    $"exceeds {PricingConstants.PursuitSendThreshold:N0}");
            }
            if (request.CrmConnections >= 2)
            {
                reasons.Add($"{request.CrmConnections} CRM connections");
            }
            return (PricingTier.Pursuit, string.Join(" and ", reasons));
        }

        return (PricingTier.Starter, "low source, send, CRM and sequencing needs");
    }

    private static decimal ComputeOnboardingFee(PricingAuditRequest request)
    {
        var fee = PricingConstants.OnboardingBaseFee;

        if (request.IntegrationComplexity.HasFlag(IntegrationComplexity.CustomAdapters))
        {
            fee += PricingConstants.OnboardingCustomAdaptersFee;
        }
        if (request.IntegrationComplexity.HasFlag(IntegrationComplexity.Sso))
        {
            fee += PricingConstants.OnboardingSsoFee;
        }
        if (request.IntegrationComplexity.HasFlag(IntegrationComplexity.IsolatedInfra))
        {
            fee += PricingConstants.OnboardingIsolatedInfraFee;
        }

        var extraCrm = Math.Max(0, request.CrmConnections - 1);
        fee += extraCrm * PricingConstants.OnboardingPerExtraCrmFee;

        return fee;
    }

    private static decimal SizeMultiplier(decimal revenue) =>
        revenue >= PricingConstants.SizeMultiplierLargeThreshold
            ? PricingConstants.SizeMultiplierLarge
            : revenue >= PricingConstants.SizeMultiplierMidThreshold
                ? PricingConstants.SizeMultiplierMid
                : PricingConstants.SizeMultiplierSmall;

    private static decimal TamMultiplier(decimal tam) =>
        tam >= PricingConstants.TamMultiplierVeryHighThreshold
            ? PricingConstants.TamMultiplierVeryHigh
            : tam >= PricingConstants.TamMultiplierHighThreshold
                ? PricingConstants.TamMultiplierHigh
                : tam >= PricingConstants.TamMultiplierMidThreshold
                    ? PricingConstants.TamMultiplierMid
                    : PricingConstants.TamMultiplierLow;

    private static decimal TierBasePrice(PricingTier tier) => tier switch
    {
        PricingTier.Starter => PricingConstants.StarterBasePrice,
        PricingTier.Pursuit => PricingConstants.PursuitBasePrice,
        PricingTier.Enterprise => PricingConstants.EnterpriseBasePrice,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown pricing tier.")
    };

    private static decimal TierFloor(PricingTier tier) => tier switch
    {
        PricingTier.Starter => PricingConstants.StarterFloor,
        PricingTier.Pursuit => PricingConstants.PursuitFloor,
        PricingTier.Enterprise => PricingConstants.EnterpriseFloor,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown pricing tier.")
    };

    private static decimal TierCeiling(PricingTier tier) => tier switch
    {
        PricingTier.Starter => PricingConstants.StarterCeiling,
        PricingTier.Pursuit => PricingConstants.PursuitCeiling,
        PricingTier.Enterprise => PricingConstants.EnterpriseCeiling,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown pricing tier.")
    };

    private static PricingTier Downgrade(PricingTier tier) => tier switch
    {
        PricingTier.Enterprise => PricingTier.Pursuit,
        PricingTier.Pursuit => PricingTier.Starter,
        PricingTier.Starter => PricingTier.Starter,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown pricing tier.")
    };

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static string Money(decimal value) =>
        RoundMoney(value).ToString("C0", CultureInfo.GetCultureInfo("en-US"));
}
