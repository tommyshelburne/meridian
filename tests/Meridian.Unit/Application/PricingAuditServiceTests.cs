using FluentAssertions;
using Meridian.Application.Pricing;

namespace Meridian.Unit.Application;

public class PricingAuditServiceTests
{
    private readonly PricingAuditService _service = new();

    /// <summary>
    /// Baseline request that scores comfortably above the ICP pursue threshold and lands
    /// in the Pursuit tier. Individual tests mutate one dimension at a time via `with`.
    /// </summary>
    private static PricingAuditRequest BaselineRequest() => new(
        TenantAnnualRevenue: 8_000_000m,
        EmployeeCount: 25,
        BdTeamSize: 3,
        SetAside: SetAsideStatus.Sdvosb,
        AddressableTam: 120_000_000m,
        ServiceablePipeline: 15_000_000m,
        SourceCount: 4,
        MonthlySendVolume: 3_000,
        CrmConnections: 1,
        IntegrationComplexity: IntegrationComplexity.None,
        NeedsMultiStepSequences: true,
        PursuitLevel: ProcurementPursuitLevel.Regularly,
        HasAdaptableCrm: true,
        DoesOutreachAlready: true);

    // ----- ICP rubric scoring -----

    [Fact]
    public void Icp_rubric_sums_every_dimension_at_maximum()
    {
        var request = BaselineRequest();

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        // revenue 25 + pursuit 20 + TAM density 20 + stack fit 15 + outreach 10 + set-aside 10
        result.Value!.IcpScore.Should().Be(100);
    }

    [Theory]
    [InlineData(8_000_000, 25)]    // in [1M, 50M]
    [InlineData(1_000_000, 25)]    // lower inclusive bound
    [InlineData(50_000_000, 25)]   // upper inclusive bound
    [InlineData(120_000_000, 15)]  // in (50M, 250M]
    [InlineData(250_000_000, 15)]  // upper inclusive bound of mid band
    [InlineData(500_000, 5)]       // below 1M
    [InlineData(300_000_000, 5)]   // above 250M
    public void Icp_revenue_band_scores_per_rubric(decimal revenue, int expectedDelta)
    {
        // A request scoring nothing else: zero out every other rubric dimension.
        var bare = BaselineRequest() with
        {
            TenantAnnualRevenue = revenue,
            // Keep TAM density at the lowest tier (5) and constant across cases.
            AddressableTam = revenue,
            PursuitLevel = ProcurementPursuitLevel.Aspirational,
            HasAdaptableCrm = false,
            DoesOutreachAlready = false,
            SetAside = SetAsideStatus.None
        };

        var score = _service.Audit(bare).Value!.IcpScore;

        // total = revenueDelta + TAM density (5, since ratio == 1)
        score.Should().Be(expectedDelta + 5);
    }

    [Theory]
    [InlineData(ProcurementPursuitLevel.Regularly, 20)]
    [InlineData(ProcurementPursuitLevel.Occasionally, 10)]
    [InlineData(ProcurementPursuitLevel.Aspirational, 0)]
    public void Icp_pursuit_level_scores_per_rubric(ProcurementPursuitLevel level, int expectedDelta)
    {
        var bare = BaselineRequest() with
        {
            TenantAnnualRevenue = 500_000m,    // revenue band -> 5
            AddressableTam = 500_000m,         // TAM density ratio 1 -> 5
            PursuitLevel = level,
            HasAdaptableCrm = false,
            DoesOutreachAlready = false,
            SetAside = SetAsideStatus.None
        };

        _service.Audit(bare).Value!.IcpScore.Should().Be(5 + 5 + expectedDelta);
    }

    [Theory]
    [InlineData(10_000_000, 20)]  // ratio 10 -> 20
    [InlineData(3_000_000, 12)]   // ratio 3 -> 12
    [InlineData(1_000_000, 5)]    // ratio 1 -> 5
    public void Icp_tam_density_scores_per_rubric(decimal tam, int expectedDelta)
    {
        var bare = BaselineRequest() with
        {
            TenantAnnualRevenue = 1_000_000m,  // revenue band -> 25
            AddressableTam = tam,
            PursuitLevel = ProcurementPursuitLevel.Aspirational,
            HasAdaptableCrm = false,
            DoesOutreachAlready = false,
            SetAside = SetAsideStatus.None
        };

        _service.Audit(bare).Value!.IcpScore.Should().Be(25 + expectedDelta);
    }

    [Fact]
    public void Icp_tam_density_treats_zero_revenue_as_zero_ratio()
    {
        var bare = BaselineRequest() with
        {
            TenantAnnualRevenue = 0m,          // revenue band -> 5, ratio guarded to 0
            AddressableTam = 50_000_000m,
            PursuitLevel = ProcurementPursuitLevel.Aspirational,
            HasAdaptableCrm = false,
            DoesOutreachAlready = false,
            SetAside = SetAsideStatus.None
        };

        // ratio 0 -> TAM density 5; revenue band 5; total 10 -> below the gate -> decline.
        var result = _service.Audit(bare);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IcpScore.Should().Be(10);
        result.Value.Decision.Should().Be(PricingDecision.Decline);
    }

    [Fact]
    public void Icp_stack_fit_and_outreach_and_setaside_each_contribute()
    {
        var withFlags = BaselineRequest() with
        {
            TenantAnnualRevenue = 500_000m,    // 5
            AddressableTam = 500_000m,         // 5
            PursuitLevel = ProcurementPursuitLevel.Aspirational, // 0
            HasAdaptableCrm = true,            // 15
            DoesOutreachAlready = true,        // 10
            SetAside = SetAsideStatus.HubZone  // 10
        };

        _service.Audit(withFlags).Value!.IcpScore.Should().Be(5 + 5 + 15 + 10 + 10);
    }

    // ----- Tier selection branches -----

    [Fact]
    public void Tier_selection_picks_enterprise_for_integration_complexity()
    {
        var request = BaselineRequest() with
        {
            IntegrationComplexity = IntegrationComplexity.Sso
        };

        _service.Audit(request).Value!.RecommendedTier.Should().Be(PricingTier.Enterprise);
    }

    [Fact]
    public void Tier_selection_picks_enterprise_for_very_high_send_volume()
    {
        var request = BaselineRequest() with
        {
            IntegrationComplexity = IntegrationComplexity.None,
            MonthlySendVolume = 60_000
        };

        _service.Audit(request).Value!.RecommendedTier.Should().Be(PricingTier.Enterprise);
    }

    [Theory]
    [InlineData(2, false, 100, 1)]   // multiple sources
    [InlineData(1, true, 100, 1)]    // multi-step sequences
    [InlineData(1, false, 600, 1)]   // send volume above pursuit threshold
    [InlineData(1, false, 100, 2)]   // multiple CRM connections
    public void Tier_selection_picks_pursuit_for_pursuit_signals(
        int sourceCount, bool multiStep, int sendVolume, int crmConnections)
    {
        var request = BaselineRequest() with
        {
            IntegrationComplexity = IntegrationComplexity.None,
            SourceCount = sourceCount,
            NeedsMultiStepSequences = multiStep,
            MonthlySendVolume = sendVolume,
            CrmConnections = crmConnections
        };

        _service.Audit(request).Value!.RecommendedTier.Should().Be(PricingTier.Pursuit);
    }

    [Fact]
    public void Tier_selection_picks_starter_when_no_pursuit_or_enterprise_signal()
    {
        var request = BaselineRequest() with
        {
            IntegrationComplexity = IntegrationComplexity.None,
            SourceCount = 1,
            NeedsMultiStepSequences = false,
            MonthlySendVolume = 400,
            CrmConnections = 1
        };

        _service.Audit(request).Value!.RecommendedTier.Should().Be(PricingTier.Starter);
    }

    // ----- Value-ceiling downgrade path -----

    [Fact]
    public void Value_ceiling_downgrades_tier_when_base_price_exceeds_capacity()
    {
        // Enterprise selected via integration complexity, but pipeline only supports Pursuit.
        // valueCeiling = pipeline * 0.04 * 0.15. To land between Pursuit base (36k) and
        // Enterprise base (72k): pipeline of 8M -> captured 320k -> ceiling 48k.
        var request = BaselineRequest() with
        {
            IntegrationComplexity = IntegrationComplexity.Sso,
            ServiceablePipeline = 8_000_000m
        };

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RecommendedTier.Should().Be(PricingTier.Pursuit);
        result.Value.Decision.Should().NotBe(PricingDecision.Decline);
        result.Value.Rationale.Should().Contain(line => line.Contains("Downgraded"));
    }

    // ----- ICP < 40 decline path -----

    [Fact]
    public void Icp_below_threshold_returns_decline_and_stops()
    {
        // revenue 5 + pursuit 0 + TAM density 5 + stack 0 + outreach 0 + set-aside 0 = 10
        var request = BaselineRequest() with
        {
            TenantAnnualRevenue = 400_000m,
            AddressableTam = 400_000m,
            PursuitLevel = ProcurementPursuitLevel.Aspirational,
            HasAdaptableCrm = false,
            DoesOutreachAlready = false,
            SetAside = SetAsideStatus.None
        };

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        var rec = result.Value!;
        rec.Decision.Should().Be(PricingDecision.Decline);
        rec.RecommendedTier.Should().Be(PricingTier.Starter);
        rec.IcpScore.Should().Be(10);
        rec.TargetAcv.Should().Be(0m);
        rec.FloorAcv.Should().Be(0m);
        rec.CeilingAcv.Should().Be(0m);
        rec.OnboardingFee.Should().Be(0m);
        rec.Rationale.Should().Contain(line => line.Contains("pursue threshold"));
    }

    // ----- Cannot-afford-Starter decline path -----

    [Fact]
    public void Cannot_afford_starter_returns_decline()
    {
        // Strong ICP (passes the gate) but a tiny serviceable pipeline.
        // valueCeiling = 500_000 * 0.04 * 0.15 = 3,000 < Starter base 9,000.
        var request = BaselineRequest() with
        {
            IntegrationComplexity = IntegrationComplexity.None,
            SourceCount = 1,
            NeedsMultiStepSequences = false,
            MonthlySendVolume = 100,
            CrmConnections = 1,
            ServiceablePipeline = 500_000m
        };

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        var rec = result.Value!;
        rec.IcpScore.Should().BeGreaterThanOrEqualTo(40);
        rec.Decision.Should().Be(PricingDecision.Decline);
        rec.TargetAcv.Should().Be(0m);
        rec.Rationale.Should().Contain(line => line.Contains("entry tier"));
    }

    // ----- Clamp behavior -----

    [Fact]
    public void Target_acv_is_clamped_up_to_the_tier_floor()
    {
        // Starter tier with ICP in the 40-59 band: rawAcv = 9000 * 0.85 * size * tam.
        // Force the smallest multipliers (size 0.90, tam 0.85) so rawAcv < Starter floor 6000.
        // rawAcv = 9000 * 0.85 * 0.90 * 0.85 = 5852.25 -> clamped up to 6000.
        var request = BaselineRequest() with
        {
            TenantAnnualRevenue = 2_000_000m,  // revenue band 25, size mult 0.90
            AddressableTam = 4_000_000m,       // TAM density: 4M/2M = 2 -> 5; tam mult 0.85
            ServiceablePipeline = 50_000_000m, // large value ceiling, no downgrade
            SourceCount = 1,
            NeedsMultiStepSequences = false,
            MonthlySendVolume = 100,
            CrmConnections = 1,
            IntegrationComplexity = IntegrationComplexity.None,
            PursuitLevel = ProcurementPursuitLevel.Aspirational, // 0
            HasAdaptableCrm = true,            // 15
            DoesOutreachAlready = false,       // 0
            SetAside = SetAsideStatus.None     // 0
        };
        // ICP = 25 + 0 + 5 + 15 + 0 + 0 = 45 -> passes the gate, in the 40-59 band.

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        var rec = result.Value!;
        rec.IcpScore.Should().Be(45);
        rec.RecommendedTier.Should().Be(PricingTier.Starter);
        rec.TargetAcv.Should().Be(6_000m);     // rawAcv 5852.25 clamped up to the Starter floor
    }

    [Fact]
    public void Target_acv_is_clamped_down_to_the_effective_ceiling()
    {
        // Enterprise tier, top multipliers: rawAcv far exceeds the Enterprise ceiling 150k.
        // rawAcv = 72000 * 1.00 * 1.10 * 1.15 = 91,080 — actually within the band; instead
        // exercise the value ceiling as the binding constraint when it sits below tier ceiling.
        var request = BaselineRequest() with
        {
            IntegrationComplexity = IntegrationComplexity.Sso, // Enterprise
            ServiceablePipeline = 12_000_000m                  // ceiling = 12M*0.04*0.15 = 72,000
        };

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        var rec = result.Value!;
        rec.RecommendedTier.Should().Be(PricingTier.Enterprise);
        // Effective ceiling = min(Enterprise ceiling 150k, value ceiling 72k) = 72k.
        rec.CeilingAcv.Should().Be(72_000m);
        rec.TargetAcv.Should().BeLessThanOrEqualTo(72_000m);
    }

    [Fact]
    public void Onboarding_fee_sums_base_integration_flags_and_extra_crm()
    {
        var request = BaselineRequest() with
        {
            IntegrationComplexity = IntegrationComplexity.CustomAdapters
                | IntegrationComplexity.Sso
                | IntegrationComplexity.IsolatedInfra,
            CrmConnections = 3   // two beyond the first
        };

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        // 5000 base + 8000 custom + 5000 sso + 12000 isolated + 2 * 2000 extra CRM = 34,000
        result.Value!.OnboardingFee.Should().Be(34_000m);
    }

    [Fact]
    public void Null_request_fails()
    {
        var result = _service.Audit(null!);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    // ----- Real fixtures -----

    [Fact]
    public void Fixture_a_parakeet_risk_lands_on_starter_with_services_attach()
    {
        var request = new PricingAuditRequest(
            // Verified best-estimate ARR ~$200K — pre-seed, ~4 employees; see Memento 875f64d2.
            TenantAnnualRevenue: 200_000m,
            EmployeeCount: 4,
            BdTeamSize: 1,
            SetAside: SetAsideStatus.None,
            AddressableTam: 40_000_000m,
            ServiceablePipeline: 2_000_000m,
            SourceCount: 1,
            MonthlySendVolume: 400,
            CrmConnections: 1,
            IntegrationComplexity: IntegrationComplexity.None,
            NeedsMultiStepSequences: false,
            PursuitLevel: ProcurementPursuitLevel.Aspirational,
            HasAdaptableCrm: true,
            DoesOutreachAlready: true);

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        var rec = result.Value!;
        rec.IcpScore.Should().Be(50);
        rec.RecommendedTier.Should().Be(PricingTier.Starter);
        rec.Decision.Should().Be(PricingDecision.PursueWithServicesAttach);
        rec.TargetAcv.Should().BeInRange(rec.FloorAcv, rec.CeilingAcv);
        rec.TargetAcv.Should().BeInRange(PricingConstants.StarterFloor, PricingConstants.StarterCeiling);
    }

    [Fact]
    public void Fixture_b_midmarket_sdvosb_lands_on_pursuit_pursue()
    {
        var request = new PricingAuditRequest(
            TenantAnnualRevenue: 8_000_000m,
            EmployeeCount: 25,
            BdTeamSize: 3,
            SetAside: SetAsideStatus.Sdvosb,
            AddressableTam: 120_000_000m,
            ServiceablePipeline: 15_000_000m,
            SourceCount: 4,
            MonthlySendVolume: 3_000,
            CrmConnections: 1,
            IntegrationComplexity: IntegrationComplexity.None,
            NeedsMultiStepSequences: true,
            PursuitLevel: ProcurementPursuitLevel.Regularly,
            HasAdaptableCrm: true,
            DoesOutreachAlready: true);

        var result = _service.Audit(request);

        result.IsSuccess.Should().BeTrue();
        var rec = result.Value!;
        rec.IcpScore.Should().Be(100);
        rec.RecommendedTier.Should().Be(PricingTier.Pursuit);
        rec.Decision.Should().Be(PricingDecision.Pursue);
        rec.TargetAcv.Should().BeInRange(rec.FloorAcv, rec.CeilingAcv);
        rec.TargetAcv.Should().BeInRange(PricingConstants.PursuitFloor, PricingConstants.PursuitCeiling);
    }
}
