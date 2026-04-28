using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Crm.HubSpot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Integration;

// End-to-end smoke against api.hubapi.com using a Private App access token.
// Setup: developers.hubspot.com → create a developer account (free) → create
// a test sandbox → Settings → Integrations → Private Apps → Create. Grant the
// scopes crm.objects.companies.write and crm.objects.deals.write at minimum.
// Copy the token, set the env var below, remove the Skip attribute.
//
// Each run leaves objects behind in the sandbox; clean up from HubSpot's
// CRM UI between runs if desired.
public class HubSpotLiveSmokeTests
{
    private const string TokenEnvVar = "MERIDIAN_HUBSPOT_PRIVATE_APP_TOKEN";

    [Fact(Skip = "Hits the live api.hubapi.com — remove Skip to run locally; "
              + "requires MERIDIAN_HUBSPOT_PRIVATE_APP_TOKEN env var.")]
    public async Task FullPortRoundTrip_AgainstLiveHubSpot()
    {
        var token = Environment.GetEnvironmentVariable(TokenEnvVar);
        token.Should().NotBeNullOrWhiteSpace($"smoke test requires {TokenEnvVar} env var");

        var adapter = new HubSpotAdapter(
            new HttpClient(),
            Options.Create(new HubSpotOptions()),
            NullLogger<HubSpotAdapter>.Instance);

        var tenantId = Guid.NewGuid();
        var ctx = new CrmConnectionContext(
            tenantId, CrmProvider.HubSpot, token!, RefreshToken: null,
            ExpiresAt: null, ApiBaseUrl: null, DefaultPipelineId: null);

        var marker = DateTimeOffset.UtcNow.Ticks.ToString();
        var orgName = $"Meridian-LiveSmoke-Co-{marker}";
        var dealTitle = $"Meridian-LiveSmoke-Deal-{marker}";

        var orgResult = await adapter.FindOrCreateOrganizationAsync(ctx, orgName, CancellationToken.None);
        orgResult.IsSuccess.Should().BeTrue($"FindOrCreateOrganization failed: {orgResult.Error}");
        orgResult.Value.Should().NotBeNullOrWhiteSpace();

        var opp = SampleOpportunity(tenantId, dealTitle);
        var dealResult = await adapter.CreateDealAsync(ctx, opp, orgResult.Value!, CancellationToken.None);
        dealResult.IsSuccess.Should().BeTrue($"CreateDeal failed: {dealResult.Error}");
        dealResult.Value.Should().NotBeNullOrWhiteSpace();

        // Notes don't require any pipeline-specific config, so they always run.
        var noteResult = await adapter.AddActivityAsync(
            ctx, dealResult.Value!, type: "",
            $"Meridian smoke note {marker}", CancellationToken.None);
        noteResult.IsSuccess.Should().BeTrue($"AddActivity (note) failed: {noteResult.Error}");

        // Stage IDs are pipeline-specific in HubSpot; only run when configured.
        var stageId = Environment.GetEnvironmentVariable("MERIDIAN_HUBSPOT_STAGE_ID");
        if (!string.IsNullOrWhiteSpace(stageId))
        {
            var stageResult = await adapter.UpdateDealStageAsync(
                ctx, dealResult.Value!, stageId, CancellationToken.None);
            stageResult.IsSuccess.Should().BeTrue($"UpdateDealStage failed: {stageResult.Error}");
        }
    }

    [Fact(Skip = "Hits the live api.hubapi.com/oauth/v1/token endpoint — remove Skip to run locally; "
              + "requires MERIDIAN_HUBSPOT_REFRESH_TOKEN, MERIDIAN_HUBSPOT_CLIENT_ID, "
              + "MERIDIAN_HUBSPOT_CLIENT_SECRET env vars.")]
    public async Task RefreshAsync_AgainstLiveHubSpot()
    {
        var refreshToken = Environment.GetEnvironmentVariable("MERIDIAN_HUBSPOT_REFRESH_TOKEN");
        var clientId = Environment.GetEnvironmentVariable("MERIDIAN_HUBSPOT_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("MERIDIAN_HUBSPOT_CLIENT_SECRET");
        refreshToken.Should().NotBeNullOrWhiteSpace();
        clientId.Should().NotBeNullOrWhiteSpace();
        clientSecret.Should().NotBeNullOrWhiteSpace();

        var broker = new HubSpotOAuthBroker(
            new HttpClient(),
            Options.Create(new HubSpotOAuthOptions
            {
                ClientId = clientId!,
                ClientSecret = clientSecret!
            }),
            NullLogger<HubSpotOAuthBroker>.Instance);

        var result = await broker.RefreshAsync(refreshToken!, CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"RefreshAsync failed: {result.Error}");
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.ApiBaseUrl.Should().BeNull(
            "HubSpot uses a fixed host; ApiBaseUrl should remain null");
    }

    private static Opportunity SampleOpportunity(Guid tenantId, string title) =>
        Opportunity.Create(
            tenantId: tenantId,
            externalId: $"smoke-{Guid.NewGuid():N}",
            source: OpportunitySource.SamGov,
            title: title,
            description: "Live smoke test from Meridian.",
            agency: Agency.Create("Smoke Agency", AgencyType.FederalCivilian, null),
            postedDate: DateTimeOffset.UtcNow.AddDays(-1),
            responseDeadline: DateTimeOffset.UtcNow.AddDays(30),
            naicsCode: "561422",
            estimatedValue: 50_000m,
            procurementVehicle: null,
            sourceDefinitionId: null);
}
