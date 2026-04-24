# Meridian

A multi-tenant platform for government BD automation: ingest procurement opportunities from federal and state portals, score them against a tenant-defined market profile, enrich point-of-contact data, and drive multi-step outreach sequences end-to-end.

**Status:** pre-GA. Targeting v1.0 MVP for end of April 2026. The codebase inherits its domain model, ports, and pipeline scaffolding from an internal predecessor (BidMatch v2); productization — multi-tenancy, generic OIDC SSO, pluggable CRM / email / source adapters, config-as-data scoring — is in active build.

## Architecture

Clean architecture, dependencies flow inward:

```
Portal / Worker → Infrastructure → Application → Domain
```

- **`Meridian.Domain`** — entities, value objects, domain rules. No external dependencies.
- **`Meridian.Application`** — ports (interfaces), pipeline orchestration, use-case services. Depends only on Domain.
- **`Meridian.Infrastructure`** — adapters: EF Core persistence (PostgreSQL), source adapters (SAM.gov, state portals), CRM clients, email providers, encryption.
- **`Meridian.Portal`** — Blazor Server UI + minimal APIs. Cookie auth + per-tenant OIDC SSO (dynamic scheme registration).
- **`Meridian.Worker`** — .NET hosted service for scheduled jobs (ingestion, scoring, sequence progression, bounce/reply processing).

Multi-tenancy is enforced at the persistence layer via EF Core global query filters keyed on `TenantId`. Every aggregate carries a tenant discriminator.

## Projects

| Path | Purpose |
|---|---|
| `src/Meridian.Domain` | Pure business model |
| `src/Meridian.Application` | Ports + orchestration |
| `src/Meridian.Infrastructure` | Adapters + persistence |
| `src/Meridian.Portal` | Blazor Server portal |
| `src/Meridian.Worker` | Background jobs |
| `tests/Meridian.Unit` | Unit tests per layer |
| `tests/Meridian.Integration` | EF + cross-tenant + auth flow tests |
| `tests/Meridian.E2E` | Portal smoke + OIDC challenge tests |

## Requirements

- .NET 9 SDK
- PostgreSQL 16+
- EF Core tools (`dotnet tool install --global dotnet-ef`)

## Getting started

```bash
# Configure a local Postgres connection
# Edit src/Meridian.Portal/appsettings.Development.json
#      src/Meridian.Worker/appsettings.Development.json

# Restore + build
dotnet build

# Run tests
dotnet test

# Run the portal (applies EF migrations on startup)
dotnet run --project src/Meridian.Portal

# Run the worker
dotnet run --project src/Meridian.Worker
```

The portal applies pending EF migrations on startup — a fresh database gets the full schema. Under the `Testing` environment the integration-test `WebApplicationFactory` swaps in an in-memory provider.

### Configuration

`appsettings.json` ships dev-only placeholders. Production deployments must override:

- `ConnectionStrings:Meridian` — PostgreSQL connection string
- `Jwt:SigningKey` — ≥ 32-char secret (the committed value is a placeholder)
- `SamGov:ApiKey` — SAM.gov v2 Opportunities API key, if ingestion is enabled
- Email provider and CRM credentials are stored per-tenant in the database, encrypted via ASP.NET Core Data Protection.

## Testing

```bash
dotnet test tests/Meridian.Unit
dotnet test tests/Meridian.Integration     # requires a reachable PostgreSQL
dotnet test tests/Meridian.E2E             # spins up the portal via WebApplicationFactory
```

Live third-party smoke tests (e.g. `MyBidMatchLiveSmokeTests`) are skipped by default and gated on credentials.

## Documentation

- [`docs/spec-product-v1-2026-04-18.md`](docs/spec-product-v1-2026-04-18.md) — current product spec (scope, domain model, adapter contracts, roadmap)
- [`docs/portal-spec-v1-2026-04-19.md`](docs/portal-spec-v1-2026-04-19.md) — current portal UX spec
- `docs/spec-v3-2026-04-14.md`, `docs/portal-spec-2026-04-14.md` — superseded, retained for historical context

## License

Not yet licensed for external use.
