namespace Meridian.Infrastructure.Crm.Salesforce;

public class SalesforceOptions
{
    public const string SectionName = "Salesforce";

    // Salesforce API version pinned at the connected-app level. Bumping this
    // requires field-mapping audit because object schemas evolve. v59.0
    // (Winter '24) is broadly available across orgs.
    public string ApiVersion { get; set; } = "v59.0";

    // Default Opportunity stage when none is specified. Production deployments
    // typically override per-tenant via CrmConnection.DefaultPipelineId or a
    // future stage-mapping table.
    public string DefaultStageName { get; set; } = "Prospecting";

    // Days from "now" used for the Opportunity.CloseDate when an opportunity
    // has no response deadline. Salesforce requires CloseDate on every
    // opportunity create.
    public int DefaultCloseDateDays { get; set; } = 30;
}

public class SalesforceOAuthOptions
{
    public const string SectionName = "Salesforce:OAuth";

    // login.salesforce.com for production orgs; test.salesforce.com for sandbox.
    public string AuthorizeUrl { get; set; } = "https://login.salesforce.com/services/oauth2/authorize";
    public string TokenUrl { get; set; } = "https://login.salesforce.com/services/oauth2/token";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // Connected app must include `refresh_token` (a.k.a. `offline_access`) for
    // the refresh-on-expiry path; otherwise refresh returns invalid_grant.
    public string Scope { get; set; } = "api refresh_token";

    public string ApiVersion { get; set; } = "v59.0";
}
