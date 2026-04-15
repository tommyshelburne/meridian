using Meridian.Application.Ports;
using Meridian.Domain.Scoring;
using Meridian.Domain.Tenants;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Persistence.Repositories;
using Meridian.Infrastructure.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMeridianInfrastructure(this IServiceCollection services, string connectionString)
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

        // Scoring
        services.AddSingleton(_ => ScoringConfig.KomBeaDefault);
        services.AddSingleton<BidScoringEngine>();
        services.AddSingleton<IScoringEngine, ScoringEngineAdapter>();

        return services;
    }
}
