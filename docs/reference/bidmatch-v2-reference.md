# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build everything
dotnet build

# Run all unit tests
dotnet test tests/KomBea.BidMatch.Unit

# Run a single test class
dotnet test tests/KomBea.BidMatch.Unit --filter "FullyQualifiedName~BidScoringEngineTests"

# Run a single test
dotnet test tests/KomBea.BidMatch.Unit --filter "FullyQualifiedName~Score_Pursue_HighConfidence"

# Run the worker (requires appsettings secrets)
dotnet run --project src/KomBea.BidMatch.Worker

# EF Core migrations
dotnet ef migrations add <Name> --project src/KomBea.BidMatch.Infrastructure --startup-project src/KomBea.BidMatch.Worker
dotnet ef database update --project src/KomBea.BidMatch.Infrastructure --startup-project src/KomBea.BidMatch.Worker
```

## Architecture

Clean Architecture with 4 layers — dependencies flow inward:

```
Worker → Infrastructure → Application → Domain
```

- **Domain** (`KomBea.BidMatch.Domain`): Pure business logic, no external dependencies.
  - `Opportunities/Opportunity.cs` — factory method validates lane fit on construction
  - `Scoring/BidScoringEngine.cs` — 5-dimension scorer; title-level lane match = 2, description-only = 1, no match = hard stop (`InvalidOperationException`)
  - `Scoring/BidScore.cs` — owned EF Core entity; Pursue ≥ 8, Partner ≥ 5, NoBid < 5
  - `Outreach/EmailThread.cs` — state machine for proposal/reply/meeting lifecycle

- **Application** (`KomBea.BidMatch.Application`): Ports (interfaces) + pipeline orchestration.
  - `Ports/` — `IOpportunitySource`, `ICrmClient`, `IProposalEngine`, `IEmailSender`, `IInboxMonitor`, `IMeetingScheduler`
  - `Pipeline/BidMatchPipelineService.cs` — fetches all sources in parallel, scores, routes Pursue/Partner to CRM+email, skips NoBid
  - `ServiceResult<T>` — used for all cross-boundary returns (no exceptions crossing service layers)

- **Infrastructure** (`KomBea.BidMatch.Infrastructure`): Adapters implementing ports.
  - `Ingestion/SamGov/SamGovClient.cs` — multi-keyword SAM.gov API search with pagination
  - `Ingestion/MyBidMatch/MyBidMatchClient.cs` + `MyBidMatchParser.cs` — Utah state procurement scraper
  - `Crm/PipedriveClient.cs` — find-or-create org, create deal, add score note
  - `Proposals/AnthropicProposalEngine.cs` — claude-haiku-4-5 with capability docs loaded from `docs/*.txt`
  - `Messaging/MicrosoftGraphClient.cs` — implements all three messaging ports (email, inbox, meetings)
  - `Persistence/BidMatchDbContext.cs` — PostgreSQL via EF Core 9; entity configs in `Persistence/Configurations/`

- **Worker** (`KomBea.BidMatch.Worker`): .NET hosted service.
  - `BidMatchWorker.cs` — daily schedule at configurable hour via `Task.Delay`
  - `Program.cs` — full DI wiring for all adapters with typed `HttpClient`s
  - `WorkerOptions` — `RunAtHour` (UTC) + `PocEmail` from `Worker` config section

## Testing

Tests are in `tests/KomBea.BidMatch.Unit/`. 98 tests, all green.

- **`FakeHandler`** — shared `HttpMessageHandler` test double defined in `SamGovIngestionTests.cs`, reused across all infrastructure tests. Constructor takes `Func<HttpRequestMessage, HttpResponseMessage>`.
- Infrastructure tests use typed `HttpClient` construction directly (no DI).
- Schema tests use SQLite in-memory (`UseInMemoryDatabase` is not used — actual Sqlite dialect via `UseSqlite("DataSource=:memory:")`).
- Pipeline tests use Moq for all ports.

## Key Scoring Rules (from KomBea calibration)

- Lane fit keywords (title): `contact center`, `call center`, `IVR`, `citizen services`, `customer service`, `helpdesk`
- Lane fit in title = 2 pts; description-only = 1 pt; none = hard stop (not in lane)
- Utah tier 2 agencies: DTS, DWS, DMV, DHHS, Department of Workforce Services
- Federal tier 1 agencies: VA, HHS, GSA, DoL, FDA, Social Security
- Scoring dimensions: lane fit (2), agency tier (2), win themes (2), past performance (2), procurement vehicle (2)

## Configuration

Secrets go in user secrets (`dotnet user-secrets set`) or environment variables. Never commit real values.
Required: `SamGov:ApiKey`, `Pipedrive:ApiToken`, `Anthropic:ApiKey`, `MicrosoftGraph:*`, `ConnectionStrings:BidMatch`.
