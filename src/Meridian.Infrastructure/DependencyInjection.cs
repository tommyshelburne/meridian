using Meridian.Application.Auth;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Auth;
using Meridian.Infrastructure.Email;
using Meridian.Application.Ingestion;
using Meridian.Application.Sources;
using Meridian.Infrastructure.Ingestion;
using Meridian.Infrastructure.Ingestion.Generic;
using Meridian.Infrastructure.Ingestion.MyBidMatch;
using Meridian.Infrastructure.Ingestion.SamGov;
using Meridian.Infrastructure.Ingestion.UsaSpending;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Persistence.Repositories;
using Meridian.Application.Opportunities;
using Meridian.Application.Outreach;
using Meridian.Application.Pipeline;
using Meridian.Infrastructure.Crm;
using Meridian.Infrastructure.Crm.HubSpot;
using Meridian.Infrastructure.Crm.Pipedrive;
using Meridian.Infrastructure.Crm.Salesforce;
using Meridian.Infrastructure.Outreach;
using Meridian.Infrastructure.Outreach.Resend;
using Meridian.Infrastructure.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMeridianInfrastructure(
        this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        // Tenant context — scoped so each request/job gets its own
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        // EF Core
        services.AddDbContext<MeridianDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Repositories
        services.AddScoped<IOpportunityRepository, OpportunityRepository>();
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IOutreachRepository, OutreachRepository>();
        services.AddScoped<IAuditLog, AuditLogRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IMarketProfileRepository, MarketProfileRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserTenantRepository, UserTenantRepository>();
        services.AddScoped<IAuthTokenRepository, AuthTokenRepository>();
        services.AddScoped<IOidcConfigRepository, OidcConfigRepository>();
        services.AddScoped<ISourceDefinitionRepository, SourceDefinitionRepository>();
        services.AddScoped<IOutboundConfigurationRepository, OutboundConfigurationRepository>();
        services.AddScoped<ICrmConnectionRepository, CrmConnectionRepository>();

        // Scoring — v2 rule-based engine
        var scoringConfig = new ScoringConfiguration();
        configuration.GetSection(ScoringConfiguration.SectionName).Bind(scoringConfig);
        services.AddSingleton(scoringConfig);
        services.AddSingleton<SeatCountEstimator>();
        services.AddSingleton<IScoringEngine, BidScoringEngine>();

        // Adapter factory — adapters register themselves as IOpportunitySourceAdapter
        services.AddTransient<ISourceAdapterFactory, SourceAdapterFactory>();

        // SAM.gov
        services.Configure<SamGovOptions>(configuration.GetSection(SamGovOptions.SectionName));
        services.AddHttpClient<SamGovClient>();
        services.AddTransient<IOpportunitySourceAdapter, SamGovClient>();
        services.AddHttpClient<SamGovPocEnricher>();
        services.AddTransient<IPocEnricher, SamGovPocEnricher>();
        services.AddHttpClient<SamGovAmendmentMonitor>();
        services.AddTransient<IBidMonitor, SamGovAmendmentMonitor>();

        // USASpending.gov
        services.Configure<UsaSpendingOptions>(configuration.GetSection(UsaSpendingOptions.SectionName));
        services.AddHttpClient<UsaSpendingClient>();
        services.AddTransient<IOpportunitySourceAdapter, UsaSpendingClient>();
        services.AddHttpClient<UsaSpendingPocEnricher>();
        services.AddTransient<IPocEnricher, UsaSpendingPocEnricher>();

        // MyBidMatch — Utah state procurement aggregator (subscription-based)
        services.AddHttpClient<MyBidMatchAdapter>();
        services.AddTransient<IOpportunitySourceAdapter, MyBidMatchAdapter>();

        // Generic adapters (tenant-configurable)
        services.AddHttpClient<GenericRssAdapter>();
        services.AddTransient<IOpportunitySourceAdapter, GenericRssAdapter>();
        services.AddHttpClient<GenericRestAdapter>();
        services.AddTransient<IOpportunitySourceAdapter, GenericRestAdapter>();
        services.AddHttpClient<GenericHtmlAdapter>();
        services.AddTransient<IOpportunitySourceAdapter, GenericHtmlAdapter>();
        services.AddSingleton<IWebhookIngestQueue, InMemoryWebhookIngestQueue>();
        services.AddTransient<IOpportunitySourceAdapter, InboundWebhookAdapter>();

        // Ingestion orchestrator
        services.AddScoped<IngestionOrchestrator>();
        services.AddScoped<SourceManagementService>();

        // Post-ingest pipeline: scoring → enrichment → CRM sync → auto-enrollment.
        // Runs as ProcessingJob in the worker between IngestionJob and SequenceJob.
        services.AddScoped<MeridianPipelineService>();

        // Secret protection — wraps ASP.NET Core Data Protection so outbound provider
        // API keys are never persisted in plaintext. The DI host (Worker / Portal)
        // must register IDataProtectionProvider with persistent key storage.
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // Outreach
        services.AddSingleton<ITemplateRenderer, LiquidTemplateRenderer>();
        services.AddSingleton<SendThrottleState>();
        services.Configure<SendThrottleOptions>(configuration.GetSection(SendThrottleOptions.SectionName));
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        services.AddScoped<ISequenceEngine, SequenceEngineService>();
        services.AddScoped<ReplyProcessor>();
        services.AddScoped<BounceProcessor>();
        services.AddScoped<OutboundConfigurationService>();
        services.AddScoped<OpportunityQueueService>();
        services.AddScoped<OpportunityDetailService>();
        services.AddScoped<ManualEnrichmentService>();
        services.AddScoped<DevSeedService>();
        services.AddSingleton<ICrmAdapter, NoopCrmAdapter>();
        services.Configure<PipedriveOptions>(configuration.GetSection(PipedriveOptions.SectionName));
        services.Configure<PipedriveOAuthOptions>(configuration.GetSection(PipedriveOAuthOptions.SectionName));
        services.AddHttpClient<PipedriveAdapter>();
        services.AddHttpClient<PipedriveOAuthBroker>();
        // Route ICrmAdapter resolution through the typed-client registration so the
        // HttpClient factory configures BaseAddress / handlers as registered above.
        services.AddTransient<ICrmAdapter>(sp => sp.GetRequiredService<PipedriveAdapter>());
        services.AddTransient<ICrmOAuthBroker>(sp => sp.GetRequiredService<PipedriveOAuthBroker>());

        services.Configure<HubSpotOptions>(configuration.GetSection(HubSpotOptions.SectionName));
        services.Configure<HubSpotOAuthOptions>(configuration.GetSection(HubSpotOAuthOptions.SectionName));
        services.AddHttpClient<HubSpotAdapter>();
        services.AddHttpClient<HubSpotOAuthBroker>();
        services.AddTransient<ICrmAdapter>(sp => sp.GetRequiredService<HubSpotAdapter>());
        services.AddTransient<ICrmOAuthBroker>(sp => sp.GetRequiredService<HubSpotOAuthBroker>());

        services.Configure<SalesforceOptions>(configuration.GetSection(SalesforceOptions.SectionName));
        services.Configure<SalesforceOAuthOptions>(configuration.GetSection(SalesforceOAuthOptions.SectionName));
        services.AddHttpClient<SalesforceAdapter>();
        services.AddHttpClient<SalesforceOAuthBroker>();
        services.AddTransient<ICrmAdapter>(sp => sp.GetRequiredService<SalesforceAdapter>());
        services.AddTransient<ICrmOAuthBroker>(sp => sp.GetRequiredService<SalesforceOAuthBroker>());
        services.AddTransient<ICrmAdapterFactory, CrmAdapterFactory>();
        services.AddTransient<ICrmOAuthBrokerFactory, CrmOAuthBrokerFactory>();
        services.AddSingleton<ICrmOAuthStateProtector, DataProtectionCrmOAuthStateProtector>();
        services.AddScoped<CrmConnectionService>();
        services.AddScoped<CrmOAuthService>();
        services.AddScoped<TenantOutboundContext>();
        services.AddSingleton<SvixSignatureVerifier>();

        // Auth services
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ITokenHasher, TokenHasher>();

        var authEmail = new AuthEmailOptions();
        configuration.GetSection(AuthEmailOptions.SectionName).Bind(authEmail);
        services.AddSingleton(authEmail);
        services.AddScoped<AuthService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<TenantManagementService>();
        services.AddScoped<OidcConfigService>();

        // Email — decorator chain (outermost to innermost):
        //   Suppression -> Throttle -> ComplianceFooter -> TenantRouted -> {Console|Resend}
        // Scoped because suppression and routing depend on per-request tenant context.
        services.AddSingleton<ConsoleEmailSender>();
        services.AddHttpClient<ResendEmailSender>();
        services.AddScoped<IEmailSender>(sp =>
        {
            var router = new TenantRoutedEmailSender(
                sp.GetRequiredService<TenantOutboundContext>(),
                sp.GetRequiredService<ConsoleEmailSender>(),
                sp.GetRequiredService<ResendEmailSender>(),
                sp.GetRequiredService<ILogger<TenantRoutedEmailSender>>());
            IEmailSender chain = router;
            chain = new ComplianceFooterEmailSender(chain,
                sp.GetRequiredService<TenantOutboundContext>());
            chain = new ThrottledEmailSender(chain,
                sp.GetRequiredService<SendThrottleState>(),
                sp.GetRequiredService<IOptions<SendThrottleOptions>>(),
                sp.GetRequiredService<ITenantContext>(),
                sp.GetRequiredService<TenantOutboundContext>(),
                sp.GetRequiredService<ILogger<ThrottledEmailSender>>());
            chain = new SuppressionFilterEmailSender(chain,
                sp.GetRequiredService<IOutreachRepository>(),
                sp.GetRequiredService<ITenantContext>(),
                sp.GetRequiredService<ILogger<SuppressionFilterEmailSender>>());
            return chain;
        });

        return services;
    }
}
