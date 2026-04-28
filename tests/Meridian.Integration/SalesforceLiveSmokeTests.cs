using FluentAssertions;
using Meridian.Application.Crm;
using Meridian.Domain.Common;
using Meridian.Domain.Opportunities;
using Meridian.Infrastructure.Crm.Salesforce;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Integration;

// End-to-end smoke against a Salesforce instance using an OAuth access token.
// Setup: developer.salesforce.com → sign up for a Developer Edition org →
// Setup → External Client Apps (or App Manager → New Connected App) → enable
// OAuth, scopes "api refresh_token", redirect URI any value (we'll bypass the
// browser flow). Use the tooling of your choice (sf cli `sf org login web`,
// or curl + the Username-Password OAuth flow) to obtain an access token and
// instance URL, then set both env vars below.
//
// Why this can't fully self-bootstrap like Pipedrive/HubSpot: Salesforce
// has no personal-token equivalent, so a one-time interactive OAuth pass is
// always required to seed the access token.
public class SalesforceLiveSmokeTests
{
    private const string TokenEnvVar = "MERIDIAN_SALESFORCE_ACCESS_TOKEN";
    private const string InstanceEnvVar = "MERIDIAN_SALESFORCE_INSTANCE_URL";

    [Fact(Skip = "Hits the live Salesforce instance — remove Skip to run locally; "
              + "requires MERIDIAN_SALESFORCE_ACCESS_TOKEN + MERIDIAN_SALESFORCE_INSTANCE_URL env vars.")]
    public async Task FullPortRoundTrip_AgainstLiveSalesforce()
    {
        var token = Environment.GetEnvironmentVariable(TokenEnvVar);
        var instanceUrl = Environment.GetEnvironmentVariable(InstanceEnvVar);
        token.Should().NotBeNullOrWhiteSpace($"smoke test requires {TokenEnvVar}");
        instanceUrl.Should().NotBeNullOrWhiteSpace($"smoke test requires {InstanceEnvVar}");

        var options = new SalesforceOptions();
        var apiBaseUrl = SalesforceOAuthBroker.BuildApiBaseUrl(instanceUrl, options.ApiVersion);

        var adapter = new SalesforceAdapter(
            new HttpClient(),
            Options.Create(options),
            NullLogger<SalesforceAdapter>.Instance);

        var tenantId = Guid.NewGuid();
        var ctx = new CrmConnectionContext(
            tenantId, CrmProvider.Salesforce, token!, RefreshToken: null,
            ExpiresAt: null, ApiBaseUrl: apiBaseUrl, DefaultPipelineId: null);

        var marker = DateTimeOffset.UtcNow.Ticks.ToString();
        var accountName = $"Meridian-LiveSmoke-Acct-{marker}";
        var oppName = $"Meridian-LiveSmoke-Opp-{marker}";

        var acctResult = await adapter.FindOrCreateOrganizationAsync(ctx, accountName, CancellationToken.None);
        acctResult.IsSuccess.Should().BeTrue($"FindOrCreateOrganization failed: {acctResult.Error}");
        acctResult.Value.Should().NotBeNullOrWhiteSpace();

        var opp = SampleOpportunity(tenantId, oppName);
        var oppResult = await adapter.CreateDealAsync(ctx, opp, acctResult.Value!, CancellationToken.None);
        oppResult.IsSuccess.Should().BeTrue($"CreateDeal failed: {oppResult.Error}");
        oppResult.Value.Should().NotBeNullOrWhiteSpace();

        // StageName values are picklist-controlled. "Qualification" is the
        // standard Developer Edition stage that follows "Prospecting".
        var stageResult = await adapter.UpdateDealStageAsync(
            ctx, oppResult.Value!, "Qualification", CancellationToken.None);
        stageResult.IsSuccess.Should().BeTrue($"UpdateDealStage failed: {stageResult.Error}");

        var taskResult = await adapter.AddActivityAsync(
            ctx, oppResult.Value!, "task",
            $"Meridian smoke test {marker}", CancellationToken.None);
        taskResult.IsSuccess.Should().BeTrue($"AddActivity failed: {taskResult.Error}");
    }

    [Fact(Skip = "Hits the live Salesforce token endpoint — remove Skip to run locally; "
              + "requires MERIDIAN_SALESFORCE_REFRESH_TOKEN, MERIDIAN_SALESFORCE_CLIENT_ID, "
              + "MERIDIAN_SALESFORCE_CLIENT_SECRET env vars.")]
    public async Task RefreshAsync_AgainstLiveSalesforce()
    {
        var refreshToken = Environment.GetEnvironmentVariable("MERIDIAN_SALESFORCE_REFRESH_TOKEN");
        var clientId = Environment.GetEnvironmentVariable("MERIDIAN_SALESFORCE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("MERIDIAN_SALESFORCE_CLIENT_SECRET");
        refreshToken.Should().NotBeNullOrWhiteSpace();
        clientId.Should().NotBeNullOrWhiteSpace();
        clientSecret.Should().NotBeNullOrWhiteSpace();

        var broker = new SalesforceOAuthBroker(
            new HttpClient(),
            Options.Create(new SalesforceOAuthOptions
            {
                ClientId = clientId!,
                ClientSecret = clientSecret!
            }),
            NullLogger<SalesforceOAuthBroker>.Instance);

        var result = await broker.RefreshAsync(refreshToken!, CancellationToken.None);

        result.IsSuccess.Should().BeTrue($"RefreshAsync failed: {result.Error}");
        result.Value!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.ApiBaseUrl.Should().StartWith("https://")
            .And.EndWith("/services/data/v59.0/",
                "Salesforce token response carries instance_url → versioned ApiBaseUrl");
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
