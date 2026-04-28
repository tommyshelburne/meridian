using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Crm.Pipedrive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Integration;

// End-to-end smoke against api.pipedrive.com using a personal API token.
// Setup: developers.pipedrive.com → create a sandbox company → Settings →
// Personal Preferences → API → "Generate token". Set the env var below and
// remove the Skip attribute to run.
//
// Each run leaves objects behind in the sandbox (Meridian-LiveSmoke-{ticks}
// org, Meridian-LiveSmoke-{ticks} deal). Acceptable in a sandbox; clean up
// from Pipedrive's UI between runs if desired.
public class PipedriveLiveSmokeTests
{
    private const string TokenEnvVar = "MERIDIAN_PIPEDRIVE_API_TOKEN";

    [Fact(Skip = "Hits the live api.pipedrive.com — remove Skip to run locally; "
              + "requires MERIDIAN_PIPEDRIVE_API_TOKEN env var.")]
    public async Task FullPortRoundTrip_AgainstLivePipedrive()
    {
        var token = Environment.GetEnvironmentVariable(TokenEnvVar);
        token.Should().NotBeNullOrWhiteSpace($"smoke test requires {TokenEnvVar} env var");

        var adapter = new PipedriveAdapter(
            new HttpClient(),
            Options.Create(new PipedriveOptions()),
            NullLogger<PipedriveAdapter>.Instance);

        var tenantId = Guid.NewGuid();
        var ctx = new CrmConnectionContext(
            tenantId, CrmProvider.Pipedrive, token!, RefreshToken: null,
            ExpiresAt: null, ApiBaseUrl: null, DefaultPipelineId: null);

        var marker = DateTimeOffset.UtcNow.Ticks.ToString();
        var orgName = $"Meridian-LiveSmoke-Org-{marker}";
        var dealTitle = $"Meridian-LiveSmoke-Deal-{marker}";

        var orgResult = await adapter.FindOrCreateOrganizationAsync(ctx, orgName, CancellationToken.None);
        orgResult.IsSuccess.Should().BeTrue($"FindOrCreateOrganization failed: {orgResult.Error}");
        orgResult.Value.Should().NotBeNullOrWhiteSpace();

        var opp = SampleOpportunity(tenantId, dealTitle);
        var dealResult = await adapter.CreateDealAsync(ctx, opp, orgResult.Value!, CancellationToken.None);
        dealResult.IsSuccess.Should().BeTrue($"CreateDeal failed: {dealResult.Error}");
        dealResult.Value.Should().NotBeNullOrWhiteSpace();

        var activityResult = await adapter.AddActivityAsync(
            ctx, dealResult.Value!, "task",
            $"Meridian smoke test {marker}", CancellationToken.None);
        activityResult.IsSuccess.Should().BeTrue($"AddActivity failed: {activityResult.Error}");

        // UpdateDealStage requires a numeric stage id from the tenant's
        // pipeline. We only run it when MERIDIAN_PIPEDRIVE_STAGE_ID is set so
        // the smoke can run against any sandbox without prior pipeline
        // configuration.
        var stageId = Environment.GetEnvironmentVariable("MERIDIAN_PIPEDRIVE_STAGE_ID");
        if (!string.IsNullOrWhiteSpace(stageId))
        {
            var stageResult = await adapter.UpdateDealStageAsync(
                ctx, dealResult.Value!, stageId, CancellationToken.None);
            stageResult.IsSuccess.Should().BeTrue($"UpdateDealStage failed: {stageResult.Error}");
        }
    }

    [Fact(Skip = "Hits the live oauth.pipedrive.com token endpoint — remove Skip to run locally; "
              + "requires MERIDIAN_PIPEDRIVE_REFRESH_TOKEN, MERIDIAN_PIPEDRIVE_CLIENT_ID, "
              + "MERIDIAN_PIPEDRIVE_CLIENT_SECRET env vars.")]
    public async Task RefreshAsync_AgainstLivePipedrive()
    {
        var refreshToken = Environment.GetEnvironmentVariable("MERIDIAN_PIPEDRIVE_REFRESH_TOKEN");
        var clientId = Environment.GetEnvironmentVariable("MERIDIAN_PIPEDRIVE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("MERIDIAN_PIPEDRIVE_CLIENT_SECRET");
        refreshToken.Should().NotBeNullOrWhiteSpace();
        clientId.Should().NotBeNullOrWhiteSpace();
        clientSecret.Should().NotBeNullOrWhiteSpace();

        var broker = new PipedriveOAuthBroker(
            new HttpClient(),
            Options.Create(new PipedriveOAuthOptions
            {
                ClientId = clientId!,
                ClientSecret = clientSecret!
            }),
            NullLogger<PipedriveOAuthBroker>.Instance);

        var result = await broker.RefreshAsync(refreshToken!, CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"RefreshAsync failed: {result.Error}");
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.ApiBaseUrl.Should().NotBeNullOrWhiteSpace(
            "Pipedrive token response carries api_domain → ApiBaseUrl");
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
