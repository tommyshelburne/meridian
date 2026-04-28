# CRM Live Verification

How to verify Meridian's CRM adapters and OAuth brokers against real provider sandboxes. None of this is required for the unit/integration suite — those run against fake HTTP handlers — but the contracts I assumed from docs need real-API confirmation before any tenant trusts the integration.

The live smoke tests are permanently `[Fact(Skip = "...")]` to keep the default suite green offline. To run a specific one, open the test file and remove the `Skip = "..."` argument from the `[Fact]` attribute.

---

## Pipedrive

**Free dev sandbox:** developers.pipedrive.com → sign in / create a developer account → "Sandbox accounts" → create a sandbox company. No paid subscription needed.

### Personal API token (covers adapter contract)

1. Sandbox company → Settings (top-right user menu) → Personal Preferences → API → "Generate token"
2. Export the token:
   ```bash
   export MERIDIAN_PIPEDRIVE_API_TOKEN=...
   ```
3. Optionally also export a stage id to exercise `UpdateDealStageAsync`:
   ```bash
   export MERIDIAN_PIPEDRIVE_STAGE_ID=4    # any numeric stage id from the default pipeline
   ```
4. Edit `tests/Meridian.Integration/PipedriveLiveSmokeTests.cs` → remove `Skip = ...` from `FullPortRoundTrip_AgainstLivePipedrive`.
5. `dotnet test tests/Meridian.Integration --filter "FullyQualifiedName~PipedriveLiveSmokeTests.FullPortRoundTrip"`

Each run leaves a `Meridian-LiveSmoke-Org-{ticks}` organization and matching deal in the sandbox.

### OAuth refresh (covers broker contract)

Requires a one-time browser pass through the OAuth flow to obtain a refresh token.

1. Pipedrive Marketplace Manager → create a private app → set redirect URI to `http://localhost:5001/crm/oauth/callback/pipedrive`
2. Run the portal locally with `Pipedrive:OAuth:ClientId` / `ClientSecret` configured, log in as a tenant, hit `/app/{slug}/crm/connect/pipedrive`, complete consent
3. Read the persisted `EncryptedRefreshToken` from the DB and the `IDataProtector` secret-purpose key (`Meridian.OutboundConfiguration.ApiKey.v1`) to extract the refresh token, OR re-run the flow and inspect the broker's `OAuthTokens` result via a debugger
4. Export:
   ```bash
   export MERIDIAN_PIPEDRIVE_CLIENT_ID=...
   export MERIDIAN_PIPEDRIVE_CLIENT_SECRET=...
   export MERIDIAN_PIPEDRIVE_REFRESH_TOKEN=...
   ```
5. Edit `tests/Meridian.Integration/PipedriveLiveSmokeTests.cs` → remove `Skip = ...` from `RefreshAsync_AgainstLivePipedrive`
6. Run

---

## HubSpot

**Free dev sandbox:** developers.hubspot.com → sign up for a developer account → create a "test sandbox" account from the dashboard. No paid Hub needed.

### Private App access token (covers adapter contract)

1. Sandbox account → Settings → Integrations → Private Apps → Create
2. Grant scopes: `crm.objects.companies.write`, `crm.objects.deals.write`. For activities, also: `crm.objects.notes.write`, `crm.objects.tasks.write`, `crm.objects.calls.write`
3. Export the token:
   ```bash
   export MERIDIAN_HUBSPOT_PRIVATE_APP_TOKEN=...
   ```
4. Optionally export a stage id:
   ```bash
   export MERIDIAN_HUBSPOT_STAGE_ID=appointmentscheduled    # any internal stage id from the default pipeline
   ```
5. Edit `tests/Meridian.Integration/HubSpotLiveSmokeTests.cs` → remove `Skip = ...` from `FullPortRoundTrip_AgainstLiveHubSpot`
6. Run

### OAuth refresh (covers broker contract)

Same pattern as Pipedrive: developer portal → create OAuth app → complete consent flow once → extract refresh token → set:

```bash
export MERIDIAN_HUBSPOT_CLIENT_ID=...
export MERIDIAN_HUBSPOT_CLIENT_SECRET=...
export MERIDIAN_HUBSPOT_REFRESH_TOKEN=...
```

Remove `Skip = ...` from `RefreshAsync_AgainstLiveHubSpot` and run.

---

## Salesforce

**Free dev org:** developer.salesforce.com → sign up for a Developer Edition org (free, doesn't expire).

Salesforce has no personal-token equivalent, so a one-time interactive OAuth pass is always required to seed the access token.

### Bootstrap an access token + instance URL

The fastest path is the [Salesforce CLI](https://developer.salesforce.com/tools/salesforcecli):

1. `sf org login web --instance-url https://login.salesforce.com` (or `https://test.salesforce.com` for a sandbox)
2. `sf org display --json --target-org <username>` → copy `accessToken` and `instanceUrl`
3. Export:
   ```bash
   export MERIDIAN_SALESFORCE_ACCESS_TOKEN=...
   export MERIDIAN_SALESFORCE_INSTANCE_URL=https://your-org.my.salesforce.com
   ```

Or use the OAuth Username-Password flow with curl against `/services/oauth2/token` — same result.

### Adapter contract

1. Edit `tests/Meridian.Integration/SalesforceLiveSmokeTests.cs` → remove `Skip = ...` from `FullPortRoundTrip_AgainstLiveSalesforce`
2. Run

The test moves the created Opportunity from the default `Prospecting` stage to `Qualification` — both stages exist in every Developer Edition org out of the box.

### OAuth refresh

1. In Setup, register a Connected App with OAuth enabled, scopes `api refresh_token`, redirect URI `http://localhost:5001/crm/oauth/callback/salesforce`
2. Complete the flow once via the portal or `sf org login web`, capture the refresh token
3. Export:
   ```bash
   export MERIDIAN_SALESFORCE_CLIENT_ID=...
   export MERIDIAN_SALESFORCE_CLIENT_SECRET=...
   export MERIDIAN_SALESFORCE_REFRESH_TOKEN=...
   ```
4. Remove `Skip = ...` from `RefreshAsync_AgainstLiveSalesforce` and run

---

## What each smoke verifies vs. what it doesn't

The smokes confirm:
- request shapes match what the provider accepts
- response shapes match what the adapter parses
- auth headers are accepted
- per-tenant base URLs (Pipedrive `api_domain`, Salesforce `instance_url`) are honored

They do **not** verify:
- the interactive consent flow (requires a browser)
- production-tier rate limits or throttling behavior
- field-mapping correctness for non-default tenant configurations

For interactive end-to-end verification of the OAuth dance, run the portal locally, hit `/app/{slug}/crm/connect/{provider}`, complete consent in a real browser, and watch the callback land tokens in `crm_connections`. That path is not covered by automated tests.
