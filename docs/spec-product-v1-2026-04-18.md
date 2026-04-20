# Meridian v1.0 — Product Specification
**Version:** 1.0 Draft
**Date:** 2026-04-18 (open-question decisions resolved 2026-04-19)
**Author:** Tommy Shelburne
**Status:** Drafting — scope + major decisions locked
**Supersedes:** `spec-v3-2026-04-14.md` (company-internal scope; now obsolete)

---

## 1. Vision

Meridian is a **plug-and-play government BD automation platform**. Any company selling into federal, state, or local government can sign up, connect their CRM and email, define their target market, and have autonomous opportunity ingestion, scoring, POC enrichment, and multi-step outreach running inside of an afternoon.

Meridian is **company-agnostic IP**. No hardcoded industries, CRMs, email providers, scoring models, target lists, or outreach copy. Every customer-specific dimension is configured through the web portal at runtime.

### 1.1 Core Value Proposition

- **30-minute onboarding.** From signup to first ingested opportunity.
- **Bring-your-own-CRM.** Pipedrive, HubSpot, and Salesforce at launch; extensible adapter layer.
- **Bring-your-own-email.** Microsoft 365, Gmail, or SMTP/API providers.
- **Bring-your-own-sources.** Built-in federal and state portal adapters, plus tenant-authored generic connectors (REST, RSS, inbound webhook, email-to-ingest, CSV) without writing code.
- **Configurable scoring.** Tenants define their Market Profile — keywords, agency weights, value thresholds, custom rules — and tune it through the portal.
- **Closed-loop outreach.** Multi-step sequences with reply/bounce detection, suppression, and CAN-SPAM compliance.

### 1.2 Non-Goals

- Not a full-CRM replacement. Meridian writes to the tenant's CRM; it does not replace it.
- Not a proposal-writing tool (v1.x). RFI/RFP drafting is roadmap.
- Not an email-blast platform. Meridian sends targeted, personalized outreach only.
- Not LinkedIn automation. Manual enrichment workflow only (ToS-safe).

### 1.3 Relationship to Prior Work

Meridian inherits its domain model, application ports, persistence layer, sequence engine, Liquid template renderer, SAM.gov adapter, and worker scaffolding from the internal v3 build (commits `b46eb79`..`728db3f`, 50 passing unit tests). The v3 scaffold is the foundation; v1.0 productization is a pivot in scope and surface area, not a rewrite.

Tenant-agnostic generalizations required:
- Remove all KomBea-specific scoring (seat-count, Nuance recompete, prime-contractor list)
- Promote CRM adapter layer and multi-tenancy to day-one features (both were deferred in v3)
- Replace Entra-only auth with generic OIDC + local credentials
- Replace Graph-only sending with pluggable email provider layer
- Add source-definition-as-data model (built-in adapters + generic connectors)

---

## 2. Product Scope

### 2.1 v1.0 GA Scope

| Capability | Detail |
|---|---|
| Multi-tenancy | Row-level `TenantId` isolation throughout. Tenant-scoped config, data, and billing. EF Core global query filters enforce boundaries. |
| Onboarding wizard | 7-step guided setup: account → org → CRM → email → market profile → sources → sequences → go live. |
| CRM adapters | Pipedrive, HubSpot, Salesforce. OAuth flow per CRM. Field mapping UI in portal. |
| Email provider adapters | Microsoft 365 (Graph OAuth delegated), SMTP (credentials — Google Workspace users connect this way via app password at GA), SendGrid / Mailgun / Postmark (API key). Gmail API OAuth deferred to v1.1 pending CASA. |
| Auth | Local email+password with TOTP 2FA. Generic OIDC (Entra, Okta, Google Workspace, Auth0) as optional per-tenant SSO. |
| Source adapters (built-in) | SAM.gov, USASpending.gov, GSA eBuy, state procurement portals (starter set: CA, TX, FL, NY, IL, VA, GA, NC, WA, CO, UT). |
| Source adapters (generic) | RSS/Atom feed, generic REST API (JSONPath field mapping + bearer/basic auth), inbound webhook (Meridian-hosted URL per source), email-to-ingest (forward to unique tenant address), CSV bulk upload. |
| Source marketplace | Portal UI to browse built-in adapters, enable per-tenant, configure parameters, add custom sources. |
| Market Profile | Tenant-defined targeting: NAICS list, keyword lists (title/description), agency tier table, value thresholds, custom regex rules. Replaces hardcoded "lane" from v3. |
| Configurable scoring | Rule-based scoring engine — ordered list of rules each returning points. Tenants define, weight, enable/disable. Starter templates provided. |
| Opportunity pipeline | Ingest → dedupe → score → POC enrich → queue for review → enroll in sequence → track to outcome. |
| POC enrichment | SAM.gov award contacts, USASpending.gov, manual portal workflow. Confidence-scored, multi-source merged. |
| Sequence engine | Multi-step outreach with configurable delays, send windows, jitter. Reply/bounce detection stops sequence. Template snapshot on enrollment (immutable). |
| Template library | Liquid templates, starter library on onboarding, per-tenant editable, versioned. |
| Send throttling | Per-tenant daily cap, per-domain rate limits, warm-up ramp. |
| Suppression + compliance | Opt-out handling, bounce suppression, CAN-SPAM compliant footer injection (physical address, unsubscribe link), one-click unsubscribe endpoint. |
| Audit log | Append-only event log per tenant. Immutable, CSV-exportable. |
| Portal | Blazor Server web app. Dashboard, Pipeline, Outreach, Contacts, Intelligence, Settings. Real-time updates via SignalR. |
| Deployment | SaaS-hosted at meridian.io. Single multi-tenant instance. Cloud-agnostic (runs on any PostgreSQL + .NET host). |

### 2.2 v1.1 Roadmap

| Capability | Detail |
|---|---|
| pgvector RAG tone adaptation | Per-contact interaction history retrieval for personalized outreach tone. |
| Open & click tracking | Pixel + redirect-wrapped links. Adds one public endpoint per tenant. |
| RFI/RFP draft generation | Claude-powered, seeded from tenant-uploaded capability docs. Human review gate. |
| Additional CRMs | Zoho, Dynamics 365, Copper, generic webhook. |
| Gmail API OAuth | Requires Google CASA Tier 2 assessment (~$15–25K/yr, 4–8 weeks). Kicked off ~6 weeks before v1.1 GA. Until then, Google Workspace users connect via SMTP + app password. |
| Additional email providers | AWS SES, Microsoft 365 app-only (client credentials) for enterprise. |
| Tier-3 scraper builder | Visual CSS-selector HTML scraper with pagination, login, JS execution support. Gated on demand and legal review per source. |
| Webhook outbound | Tenant subscribes to Meridian events (opportunity scored, reply received) via webhook. |

### 2.3 v1.2 Roadmap

| Capability | Detail |
|---|---|
| Self-hosted deployment | Docker-free single-binary + Postgres install for enterprise air-gapped environments. Per-instance license. |
| Billing / packaging | Stripe integration. Per-tenant plans, usage metering (opportunities ingested, emails sent), trial management. |
| White-label branding | Per-tenant portal theming, custom domain support. |
| API + SDK | Public REST API for programmatic access. Optional .NET and TypeScript client SDKs. |

### 2.4 Pricing & Packaging

Per-org pricing, usage-capped. Per-seat pricing rejected because gov BD teams are small (1–5 users) and per-seat caps TAM.

| Plan | Price (monthly) | Key limits |
|---|---|---|
| **Trial** | Free, 14 days | Full features; 100 opportunities ingested, 50 emails sent, inbound-webhook + email-to-ingest disabled |
| **Starter** | $99 | 1 CRM connection, 1 Market Profile, 3 sources, 500 opps/mo, 500 sends/mo, 3 users, 500 inbound custom-source events/mo |
| **Pro** | $299 | Unlimited sources, 3 Market Profiles, 2K opps/mo, 2K sends/mo, 10 users, custom REST connectors, OIDC SSO, 5K inbound custom-source events/mo |
| **Enterprise** | Custom | Unlimited usage, SLA, dedicated support, self-hosted option (v1.2) |

Billing enforcement infrastructure (Stripe) ships in v1.2. v1.0 GA is revenue-disabled — free access for design-partner tenants. v1.1 adds soft-cap warnings. Data model carries `PlanTier` and `Status` from day one so no retroactive migration is required.

Starter trial caps (100 opps, 50 sends) are initial guesses; measure after first 10 tenants and recalibrate before GA.

### 2.5 Explicitly Out of Scope

- LinkedIn automated scraping (ToS violation)
- GovWin / Bloomberg Gov paid-data integration (customer-BYO if licensed)
- Phone / SMS outreach
- Calendar management of any kind
- Bulk email blast functionality

---

## 3. Architecture

### 3.1 Guiding Principles

1. **Clean Architecture.** Domain → Application → Infrastructure → Worker/Portal. Dependencies flow inward. Ports defined in Application, adapters in Infrastructure.
2. **Multi-tenancy is a first-class invariant, not a feature.** Every entity carries `TenantId`. Every query is tenant-scoped by default. Leaking tenant data is a P0 defect class.
3. **Config-as-data.** Everything that varies per customer lives in the database (sources, scoring rules, templates, sequences, field mappings). Code ships one tenant-agnostic implementation.
4. **Ports stay thin.** New capabilities = new port + new adapter. Orchestration never references concrete implementations.
5. **Explicit failure propagation.** `ServiceResult<T>` at cross-boundary calls. No swallowed exceptions.
6. **Immutable audit log.** All state-changing operations append an audit event. Nothing is ever deleted from the log.

### 3.2 Solution Structure (largely preserved from v3)

```
Meridian.sln
├── src/
│   ├── Meridian.Domain/              # Entities, value objects, domain events
│   ├── Meridian.Application/         # Ports, pipeline orchestration, CQRS handlers
│   ├── Meridian.Infrastructure/      # Adapters: sources, CRM, email, persistence, RAG
│   ├── Meridian.Worker/              # .NET hosted service (scheduled jobs)
│   └── Meridian.Portal/              # Blazor Server + API
└── tests/
    ├── Meridian.Unit/
    ├── Meridian.Integration/
    └── Meridian.E2E/
```

### 3.3 Domain Model (tenant-agnostic)

Changes from v3:
- `LaneConfiguration` → `MarketProfile`
- `Opportunity.EstimatedSeats` removed (KomBea-specific); replaced with generic `Opportunity.EstimatedValue` + `Opportunity.DerivedMetrics` (JSON, tenant-rule outputs)
- `Tenant.SendingIdentity` → `Tenant.EmailProviderConfig` (typed discriminated union)
- New: `SourceDefinition`, `SourceAdapterType`, `CrmConnection`, `EmailProviderConfig`, `ScoringRule`, `OidcConfig`

```csharp
Tenant {
    TenantId: Guid
    Name: string
    Slug: string                      // URL-safe, used for inbound webhooks & unique email addresses
    Plan: PlanTier                    // Free, Starter, Pro, Enterprise (v1.2 enforced)
    Status: TenantStatus              // Trial, Active, Suspended, Cancelled
    CreatedAt: DateTimeOffset
}

MarketProfile {
    MarketProfileId: Guid
    TenantId: Guid
    Name: string                      // "Federal IT Services", "State Contact Center", etc.
    NaicsCodes: string[]
    TitleKeywords: string[]
    DescriptionKeywords: string[]
    AgencyTiers: AgencyTier[]         // tenant-defined, each with name + tier rank
    ValueThresholds: ValueThreshold[] // e.g. ≥ $1M, ≥ $10M — feeds custom rules
    IsActive: bool
}

SourceDefinition {
    SourceDefinitionId: Guid
    TenantId: Guid                    // nullable for global built-ins; cloned per tenant on enable
    AdapterType: SourceAdapterType    // SamGov, UsaSpending, StatePortal, GenericRest, GenericRss,
                                      // InboundWebhook, EmailInbox, CsvUpload
    Name: string
    ParametersJson: JsonDocument      // adapter-specific config (API keys, URLs, field maps)
    Schedule: CronExpression?         // null for push-based sources
    IsEnabled: bool
    LastRunAt: DateTimeOffset?
    LastRunStatus: SourceRunStatus
}

ScoringRule {
    ScoringRuleId: Guid
    TenantId: Guid
    Name: string
    OrderIndex: int
    RuleType: ScoringRuleType         // KeywordMatch, AgencyTierMatch, ValueThreshold, RegexMatch,
                                      // NaicsMatch, VehicleMatch, CustomExpression
    ConfigJson: JsonDocument          // rule-specific config
    Points: int                       // positive or negative
    IsEnabled: bool
}

CrmConnection {
    CrmConnectionId: Guid
    TenantId: Guid
    Provider: CrmProvider             // Pipedrive, HubSpot, Salesforce
    AuthTokenEncrypted: byte[]
    RefreshTokenEncrypted: byte[]?
    ExpiresAt: DateTimeOffset?
    FieldMappings: CrmFieldMapping[]  // Meridian field → CRM field per entity
    DefaultPipelineId: string         // CRM-side pipeline to write deals into
    IsActive: bool
}

EmailProviderConfig {
    EmailProviderConfigId: Guid
    TenantId: Guid
    Provider: EmailProvider           // MicrosoftGraphOAuth, GmailOAuth, Smtp, SendGrid, Mailgun, Postmark
    FromAddress: string
    FromDisplayName: string
    CredentialsEncrypted: JsonDocument
    DailySendCap: int
    SendWindowStart: TimeSpan
    SendWindowEnd: TimeSpan
    SendWindowJitterMinutes: int
    WarmupMode: bool
}

Opportunity {
    OpportunityId: Guid
    TenantId: Guid
    SourceDefinitionId: Guid
    ExternalId: string
    Title: string
    Description: string
    Agency: Agency
    EstimatedValue: decimal?
    PostedDate: DateTimeOffset
    ResponseDeadline: DateTimeOffset?
    NaicsCode: string
    ProcurementVehicle: string?
    DerivedMetrics: JsonDocument      // outputs from custom scoring rules
    Score: BidScore?
    Status: OpportunityStatus
    WatchedSince: DateTimeOffset?
    LastAmendedAt: DateTimeOffset?
    Contacts: OpportunityContact[]
    AuditEvents: AuditEvent[]
}

// Contact, OutreachSequence, SequenceStep, OutreachEnrollment,
// EmailActivity, AuditEvent, SuppressionEntry — carried from v3 with TenantId scoping
```

### 3.4 Application Ports

```csharp
// Ingestion
IOpportunitySourceAdapter            // resolves per SourceAdapterType; takes SourceDefinition, yields Opportunities
ISourceAdapterFactory                // resolves adapter by type
IBidMonitor                          // amendments on watched opps

// Scoring
IScoringEngine                       // generic rule-based evaluation

// Enrichment
IPocEnricher                         // multi-adapter fan-out
IContactVerifier                     // MX + SMTP probe

// Outreach
IEmailSender                         // generic; decorated with throttle + suppression
IInboxMonitor                        // reply/bounce detection per email provider
ISequenceEngine                      // evaluate enrollments, dispatch sends
ITemplateRenderer                    // Liquid

// CRM
ICrmAdapter                          // resolves per CrmProvider; find-or-create org/deal, log activity
ICrmAdapterFactory

// Auth
IAuthProvider                        // local / OIDC
ITenantContext                       // ambient current-tenant resolver

// Persistence
IOpportunityRepository
IContactRepository
IOutreachRepository
ITenantRepository
ISourceDefinitionRepository
IScoringRuleRepository
IAuditLog
```

### 3.5 Source Adapter Tiered Design (Tier 2)

Source adapters fall into two categories:

**Built-in adapters** — code ships with the product; tenants enable and parameterize.

| Adapter | Parameters (portal-editable) |
|---|---|
| SAM.gov | NAICS filter, keyword filter, posted-date lookback, API key (optional) |
| USASpending.gov | Agency filter, NAICS filter, award-type filter |
| GSA eBuy | Login credentials (encrypted), category filter |
| State Portals (per state) | Per-state config: keyword/category filter, credentials where required |

**Generic adapters** — code is tenant-agnostic; tenant provides full config via portal.

| Adapter Type | Config Schema |
|---|---|
| `GenericRss` | Feed URL, polling cadence, field mapping (RSS `item.title` → `opportunity.title`, etc.) |
| `GenericRestApi` | Endpoint URL, HTTP method, auth (none / API key with custom header / bearer / HTTP basic / OAuth 2 client credentials — Meridian handles token refresh), request params/body template, response JSONPath field mapping, pagination config, polling cadence. **Not supported v1.0:** OAuth 2 authorization code flow, HMAC request signing, mTLS — these require per-API custom adapters. |
| `InboundWebhook` | Auto-generated unique URL (`https://meridian.io/ingest/webhook/{tenant-slug}/{source-id}`), signing secret (HMAC-SHA256, rotatable in portal), field mapping for JSON payload. Tenant's system POSTs opportunities. Rate limit 100 req/min and 10K req/day per source, plan-capped monthly. Payload size cap 1 MB. Invalid signatures return 404 with no body (no oracle). |
| `EmailInbox` | Auto-generated unique address (`ingest+{tenant-slug}-{source-id}@meridian.io`). SPF/DKIM/DMARC verification required on inbound — any failure rejects. Optional per-source sender-domain allowlist. Parses solicitation emails (subject/body → fields via regex; LLM fallback v1.1). First 10 emails from any new sender domain are quarantined for portal review. Rate limit 100 emails/day/source, plan-capped monthly. |
| `CsvUpload` | Portal upload UI, CSV column → opportunity field mapping, one-shot or scheduled re-upload. |

Every adapter returns a uniform `IngestedOpportunity` DTO. Deduplication by `(TenantId, SourceDefinitionId, ExternalId)` uniqueness + cross-source fuzzy matching on (agency, title, posted-date).

**Abuse prevention — cross-cutting on ingest surfaces:**
- Every ingest logged to audit log with source metadata (IP, signature result, email headers, content-length)
- A source auto-disables after N consecutive failures (default 5); tenant alerted in portal + email
- Source health page in portal: recent runs, error rate, ingest volume trend
- No source code path can create an `EmailActivity` directly — ingestion produces `Opportunity` / `Contact` only, so a malicious ingest cannot relay mail through Meridian
- Plan-tier caps on inbound custom-source events are enforced at intake (see §2.4)

### 3.6 CRM Adapter Layer

Each CRM adapter implements `ICrmAdapter` with:

```csharp
Task<CrmOrg> FindOrCreateOrgAsync(OrgInput input, CancellationToken ct)
Task<CrmDeal> FindOrCreateDealAsync(DealInput input, CancellationToken ct)
Task UpdateDealStageAsync(string dealId, string stageId, CancellationToken ct)
Task LogActivityAsync(ActivityInput input, CancellationToken ct)
Task<IReadOnlyList<CrmPipeline>> ListPipelinesAsync(CancellationToken ct)
Task<CrmFieldSchema> GetFieldSchemaAsync(CancellationToken ct)
```

OAuth flow per CRM handled by dedicated `ICrmOAuthBroker` adapter — shared app registration in Meridian's own identity (single OAuth app per CRM), tenant grants consent. Refresh tokens rotated automatically.

Field mapping UI in portal: Meridian fields (deal_name, value, stage, custom_fields) → CRM fields (per tenant's CRM schema, fetched via `GetFieldSchemaAsync`).

### 3.7 Email Provider Adapter Layer

`IEmailSender` implementations:

| Adapter | Auth | Use Case |
|---|---|---|
| `MicrosoftGraphOAuthSender` | Delegated OAuth (user consents their mailbox) | M365 users — "send from my mailbox" |
| `SmtpEmailSender` | Host + port + credentials | Fallback for any SMTP provider. **Google Workspace path at GA** via app password + `smtp.gmail.com:587`. Onboarding wizard includes step-by-step app-password guide. |
| `SendGridEmailSender` | API key | High-volume transactional |
| `MailgunEmailSender` | API key | Alt to SendGrid |
| `PostmarkEmailSender` | API key | Alt to SendGrid, strong deliverability |

Gmail API OAuth (`GmailOAuthSender`) is a v1.1 adapter — blocked on CASA Tier 2 assessment (§2.2, §9 item 1).

All adapters decorated with:
- `ThrottledEmailSender` — enforces daily cap, send window, jitter, per-domain rate
- `SuppressionFilter` — blocks opt-outs, bounces, suppression list
- `ComplianceInjector` — injects CAN-SPAM footer (physical address, unsubscribe link) on the fly if missing

**Reply threading — hybrid model.** Two modes:

1. **Native threading** for `MicrosoftGraphOAuthSender` at GA (and `GmailOAuthSender` when it lands in v1.1). Meridian polls the tenant's mailbox via the same OAuth connection and matches replies by `Message-Id` / `In-Reply-To`. Preserves the tenant's existing threading UX in their mailbox.
2. **Reply-proxy** for every other sender (`SmtpEmailSender` including Google Workspace app-password path, `SendGridEmailSender`, `MailgunEmailSender`, `PostmarkEmailSender`). Outbound `Reply-To` header rewritten to a per-enrollment proxy address `reply+{token}@reply.meridian.io`. Replies land on Meridian-hosted inbound infrastructure, token matches the `OutreachEnrollment`, the reply is recorded as an `EmailActivity` of type `Replied`, and (optional per-tenant setting) forwarded to the tenant's original From address for visibility in their own inbox.

The reply-proxy requires Meridian-hosted inbound MX on `reply.meridian.io`. v1.0 uses **SendGrid Inbound Parse** (single inbound endpoint → webhook → token lookup). DKIM alignment on `reply.meridian.io` to keep spam-filter heuristics happy. The Reply-To ≠ From pattern is the same one Outreach, SalesLoft, and Apollo run at scale — precedent is solid.

Bounce detection follows the same split: native-threading senders pick up NDRs from the same mailbox poll; proxy-mode senders detect bounces via ESP webhooks (SendGrid Event Webhook, Mailgun webhooks, Postmark InboundStream). SMTP sender bounces rely on the reply-proxy catching NDRs into the proxy address — adequate for most but not all bounce types; tenant alerted if bounce visibility is degraded.

### 3.8 Auth Model

**Local auth:** email + password (bcrypt), mandatory TOTP 2FA on Starter plan and up.

**OIDC per tenant (optional):** tenant admin configures OIDC issuer URL, client ID, client secret in portal. Supports Entra ID, Okta, Google Workspace, Auth0, any compliant OIDC provider. Users redirected to IdP for login.

**Session management:** JWT access token (15 min) + refresh token (7 days) stored HTTP-only. Tenant context resolved from token claim at every request.

**Roles (v1.0):**
- **Owner** — billing, tenant settings, user management
- **Admin** — all operational config (sources, sequences, scoring, etc.)
- **Operator** — pipeline operations (review opps, manual enrichment, log calls)
- **Viewer** — read-only

### 3.9 Scoring Engine (rule-based)

Replaces v3's hardcoded dimensions. Tenants build their scoring by ordering a list of rules; each rule returns points for a given opportunity. Final score = sum of rule points, clamped ≥ 0.

Built-in rule types:

| Rule Type | Config | Example |
|---|---|---|
| `KeywordMatch` | Field (title/description/both), keywords, points-per-match, cap | "title contains 'contact center' or 'call center' → +3" |
| `NaicsMatch` | NAICS codes, points | "NAICS 541512 → +2" |
| `AgencyTierMatch` | Agency tier from MarketProfile → points | "Tier 1 agency → +3" |
| `ValueThreshold` | Comparator (≥, ≤, range), points | "EstimatedValue ≥ $1M → +2" |
| `VehicleMatch` | Procurement vehicles, points | "GSA MAS → +1" |
| `RegexMatch` | Field, regex pattern, points | "description matches /recompete|renewal/i → +1" |
| `CustomExpression` | JSONata or similar safe expression | For advanced users, v1.1 |

Thresholds per MarketProfile:
- **Pursue:** score ≥ tenant-configured (default 10)
- **Partner:** score ≥ tenant-configured (default 6)
- **NoBid:** below Partner threshold

Starter rule packs (one-click apply on onboarding):
- "Federal IT Services"
- "State/Local Contact Center"
- "Professional Services (Generic)"
- "Construction & Trades"
- Blank (build from scratch)

### 3.10 Sequence Engine

Unchanged from v3 (design is already tenant-agnostic):
- Enrollment is explicit
- Steps conditional on send window, daily cap, reply/bounce/opt-out status
- Reply/bounce stops sequence
- Pause/resume is human-controlled
- Template snapshot on enrollment (immutable)
- Liquid templates with token substitution

### 3.11 Persistence

PostgreSQL 18 + EF Core 10. Tenant-scoped row-level with global query filters. pgvector extension reserved for v1.1 RAG (not required for v1.0).

Partitioning:
- `audit_events` — monthly partitions
- `email_activities` — monthly partitions at Starter+

Archival:
- Opportunities older than 12 months with status `NoBid` or `Closed` → `opportunities_archive` (nightly job)

Encryption:
- CRM tokens, email credentials, OIDC secrets — AES-256 at rest via ASP.NET Data Protection, key per tenant

### 3.12 Deployment — SaaS-First

v1.0: single multi-tenant SaaS instance at `meridian.io`.

**Infrastructure:**
- ASP.NET Core 10 on Kestrel behind nginx / Cloudflare
- PostgreSQL 18 (managed provider: AWS RDS, Azure Flexible Server, or self-run on a single box at launch)
- Redis for distributed cache + SignalR backplane
- Blob storage for CSV uploads, template attachments (S3 or Azure Blob)
- SendGrid for Meridian's own transactional email (signup verification, password reset, notifications) **and** reply-proxy Inbound Parse (`reply.meridian.io`)
- DNS: `meridian.io` (portal + API), `reply.meridian.io` (inbound MX for reply-proxy), `track.meridian.io` reserved for open/click tracking endpoints (v1.1)

**Worker scaling:**
- Single worker instance at launch
- Jobs iterate all active tenants (existing v3 pattern preserved)
- Horizontal scaling via DB-backed job claim (v1.1 if needed)

**Self-hosted (v1.2):**
- Single .NET binary + Postgres connection string
- Offline license key validation
- No multi-tenancy required in self-host mode (enterprise runs one tenant per instance)

### 3.13 Background Jobs

| Job | Cadence | Scope |
|---|---|---|
| `SourceIngestionJob` | Per-source cron | One job run per enabled source across all tenants |
| `BidMonitorJob` | 12:00 UTC daily | Amendments on watched opps |
| `SequenceJob` | Every 30 min | Evaluate enrollments tenant-by-tenant |
| `InboxMonitorJob` | Every 15 min (polling providers); continuous (webhook providers) | Replies/bounces |
| `PocEnrichmentJob` | 08:00 UTC daily | Fan-out enrichment for unenriched Pursue/Partner opps |
| `AuditArchiveJob` | 02:00 UTC daily | Partition rollover, archival |
| `TrialExpiryJob` | 06:00 UTC daily | (v1.2) notify expiring trials, suspend |

---

## 4. Plug-and-Play Onboarding

The onboarding flow is the product's front door. Target: **signup → first ingested opportunity in 30 minutes**.

### 4.1 Flow

1. **Sign up.** Email + password (or OIDC if tenant preconfigured). Email verification.
2. **Organization setup.** Org name, slug (auto-suggested from email domain), time zone. Tenant provisioned.
3. **Connect CRM.** Choose provider → OAuth redirect → consent → return. Portal fetches pipelines, prompts to select default pipeline. Auto-maps common fields (deal_name, value, stage); flags unmapped fields for review.
4. **Connect email.** Choose provider → OAuth (or credentials for SMTP/API-key providers). Send test email to the signup address to verify. Display current SPF/DKIM/DMARC status with remediation links.
5. **Create Market Profile.** Pick a starter template (federal IT / state CC / professional services / construction / blank). Review & edit NAICS, keywords, agency tiers.
6. **Enable sources.** Multi-select built-in adapters with inline config (NAICS filter prefilled from Market Profile). Optionally add first generic source (RSS/REST/webhook).
7. **Import starter templates & sequence.** Pick a sequence preset (5-touch / 3-touch / single-send) using a starter template pack. Preview rendered output against a sample opportunity.
8. **Go live.** Review summary card. Click "Activate." First source run kicks off within 5 minutes.

### 4.2 Onboarding Safety Rails

- Sending is **dry-run by default** for the first 24h — emails rendered and logged but not dispatched. Tenant flips a visible switch to go live.
- Daily cap starts at **20 sends/day** regardless of configured target. Ramps automatically by 10/day if warm-up mode is on.
- No sequence can be enrolled until at least one template has been previewed (forces human review before machine-sent outreach).

### 4.3 Connector Marketplace (Portal)

Dedicated portal page browsable during and after onboarding:

- **CRMs** — Pipedrive, HubSpot, Salesforce (more coming)
- **Email** — Microsoft 365, Gmail, SMTP, SendGrid, Mailgun, Postmark
- **Sources — Built-in** — SAM.gov, USASpending, GSA eBuy, per-state portals
- **Sources — Generic** — RSS, REST API, Inbound Webhook, Email-to-Ingest, CSV

Each tile: description, screenshot, setup time estimate, "Enable" CTA.

---

## 5. Compliance & Legal

### 5.1 CAN-SPAM

- Physical mailing address injected into every email footer (tenant-configured in Settings)
- One-click unsubscribe link in every email routed to Meridian-hosted endpoint (`https://meridian.io/u/{token}`)
- Unsubscribes processed immediately (< 1 minute), not the 10-business-day CAN-SPAM maximum
- No deceptive subject lines — templates reviewed at save time for common violations (v1.1 automated check)

### 5.2 GDPR / Data Handling

- Tenant data isolated by `TenantId` — audited at app layer, verifiable via DB queries
- Data export self-service (Settings → Data Export, CSV/JSON zip)
- Data deletion self-service on cancellation with 30-day grace
- Subprocessor list maintained and published at `meridian.io/subprocessors`
- US-only data residency at GA (US-hosted Postgres, US-based subprocessors). EU residency deferred to v1.2+ — any EU-residency prospect is treated as an enterprise deal requiring custom engagement, not a self-serve feature.
- US state privacy laws (CCPA, Virginia CDPA, Colorado CPA, etc.) applicable to Meridian's own customer data — DPA and privacy policy to be drafted before GA.

### 5.3 Source ToS

Built-in adapters audited for ToS compliance per source. Tenants are responsible for ToS on custom sources they configure. Portal shows a ToS-acknowledgement checkbox on generic adapter setup.

### 5.4 Gov Outreach Ethics

- No impersonation of agency personnel
- All outreach clearly identifies sender organization
- No false urgency or false-relationship claims
- Sequences stop immediately on opt-out or reply

---

## 6. Success Metrics (product-level)

| Metric | v1.0 Target (first 90 days post-GA) |
|---|---|
| Signups | 50 |
| Conversion to paid (when billing lands) | 20% of signups |
| Time-to-first-ingestion (median) | ≤ 30 min |
| Time-to-first-send (median) | ≤ 2 hours |
| Onboarding completion rate | ≥ 70% |
| Weekly active tenants | ≥ 25 by end of quarter |
| Support tickets per active tenant per month | ≤ 1 |
| P0 bugs | 0 |
| P1 bugs | ≤ 3 open at any time |

Per-tenant health metrics surfaced in portal:
- Opportunities ingested/week
- POC enrichment rate
- Emails sent/week, reply rate, bounce rate
- Deals created in CRM, deals closed
- Sequence completion vs. reply-stopped ratio

---

## 7. Build Plan

### 7.1 Current State (as of 2026-04-18)

- Solution scaffolded (`Meridian.sln`)
- Domain, Application, Infrastructure, Worker, Portal projects exist
- 50 passing unit tests across Domain/Application
- Built: SAM.gov source adapter, POC enricher, amendment monitor, Liquid renderer, sequence engine, send throttle, worker DI wiring
- Entities in place (tenant-scoped): Opportunity, Contact, OutreachSequence, OutreachEnrollment, EmailActivity, AuditEvent

### 7.2 Migration from v3 → v1.0 Product

**Delete / generalize:**
- `SeatCountEstimate`, `EstimatedSeats`, seat-count scoring logic
- Prime-contractor target list
- Nuance recompete detection (replace with generic regex-rule recompete detection)
- `outreach@kombea.com` references
- KomBea-tenant bootstrap seed data (move to example-only appendix)

**Rename:**
- `LaneConfiguration` → `MarketProfile`
- `SendingIdentity` → `EmailProviderConfig`

**Extend:**
- `ICrmClient` → `ICrmAdapter` with full shape (§3.6)
- Add `SourceDefinition` entity + `ISourceAdapterFactory`
- Add `ScoringRule` entity + rule engine (replaces hardcoded `BidScoringEngine`)
- Add `CrmConnection`, `EmailProviderConfig` entities
- Add auth model: users, roles, OIDC config

**New:**
- Onboarding API + portal flow
- Generic source adapters (RSS, REST, inbound webhook, email-to-ingest, CSV)
- OAuth brokers for CRM and email
- Billing hooks (data model only in v1.0; enforcement in v1.2)

### 7.3 Phases

**Week 0 — Long-lead items (starts immediately, runs in parallel with Phase 1)**
- Begin **Microsoft Graph publisher verification** + MPN ID. 1–2 weeks, free. Blocks M365 OAuth to tenants beyond a small test pool.
- Register private Connected Apps (Salesforce), OAuth apps (HubSpot, Pipedrive) — immediate, no review required. Marketplace listings deferred to v1.1.
- Draft DPA, privacy policy, subprocessor list. Legal review.
- Write the **"Connect Google Workspace via SMTP app password" guide** — required for the v1.0 Gmail-user path.

**Deferred — Google CASA Tier 2 assessment for Gmail OAuth.** Not v1.0. Kicked off ~6 weeks before v1.1 GA. Saves ~$15–25K/yr and de-risks the v1.0 timeline. Google Workspace users in v1.0 use SMTP + app password.

**Phase 1 — Multi-tenant foundation (Weeks 1-2)**
- Strip KomBea-isms, rename, migrate schema
- Auth (local + OIDC) with role system
- Tenant provisioning API
- Row-level tenancy filters everywhere, verified by integration tests

**Phase 2 — Source system (Weeks 3-4)**
- `SourceDefinition` model + `ISourceAdapterFactory`
- Refactor SAM.gov adapter to the new contract
- Add USASpending adapter
- Build first 3 generic adapters: RSS, REST API, Inbound Webhook
- Portal: Source Marketplace + per-source config UI

**Phase 3 — CRM + email (Weeks 5-6)**
- Pipedrive, HubSpot, Salesforce adapters + OAuth brokers
- Microsoft Graph OAuth + SMTP + SendGrid + Mailgun + Postmark senders (Gmail OAuth deferred to v1.1)
- **Reply-proxy infrastructure**: `reply.meridian.io` MX + SendGrid Inbound Parse + token matcher + enrollment lookup + optional forward-to-tenant
- Bounce detection: native-mailbox for Graph OAuth, ESP webhooks for SendGrid/Mailgun/Postmark, proxy-inbox NDR parsing for SMTP
- Portal: Connect CRM, Connect Email wizards with field mapping and live test-send. Gmail connection flow shows the app-password walkthrough inline.

**Phase 4 — Scoring + market profiles (Week 7)**
- Rule-based scoring engine
- Starter rule packs
- Portal: Market Profile editor, Scoring rule builder

**Phase 5 — Onboarding + portal polish (Week 8)**
- Full 7-step onboarding wizard
- Dashboard, Pipeline, Outreach, Contacts, Intelligence, Settings pages
- Dry-run safety rails + warm-up mode

**Phase 6 — Launch prep (Week 9)**
- Add 5 more state portal adapters (beyond Utah)
- Compliance: CAN-SPAM footer injection, unsubscribe endpoint
- Audit log partition job, archival job
- End-to-end smoke tests against real tenants

**GA target:** ~10 weeks from spec lock, adjusting for scope discovery.

### 7.4 Development Environment

- Design + spec: Memento + Claude Code on MacBook Air M4
- Build + test + deploy: whatever Claude Code harness is preferred
- PR review: Claude Code code-reviewer agent
- Test discipline: 95% line + branch coverage enforced in CI, BDD scenarios for tenant-facing features

---

## 8. Decisions Log (resolved 2026-04-19)

| # | Decision | Status |
|---|---|---|
| 1 | Per-org pricing: Free trial / $99 Starter / $299 Pro / Enterprise. Billing enforcement v1.2. | Locked (§2.4) |
| 2 | US-only data residency at GA; EU residency deferred to v1.2+. | Locked (§5.2) |
| 3 | Reply threading: native for Graph/Gmail OAuth, Meridian-hosted reply-proxy for all other senders via `reply.meridian.io` + SendGrid Inbound Parse. | Locked (§3.7, §3.12) |
| 4 | MS Graph publisher verification Week 0. CRM OAuth as private apps at GA, no marketplace listings. Gmail OAuth scope-cut from v1.0 → deferred to v1.1 (Google Workspace users connect via SMTP + app password at GA). CASA kicked off ~6 weeks before v1.1 GA. | Locked (§2.2, §7.3) |
| 5 | 11-state starter set at GA (CA, TX, FL, NY, IL, VA, GA, NC, WA, CO, UT); 15-state pack v1.1; remainder gap-filled via generic connectors. | Locked (§2.1) |
| 6 | Generic REST adapter auth: None / API Key / Bearer / Basic / OAuth 2 client credentials. Auth-code flow, HMAC signing, mTLS out of v1.0. | Locked (§3.5) |
| 7 | Abuse prevention: HMAC signatures + rate limits + DMARC verification + plan-tier caps + quarantine for new sender domains. | Locked (§3.5) |

## 9. Still Open

1. **Open/click tracking in v1.0?** Reply-proxy infra covers most of what's needed (public subdomain, token matcher). Cost to add pixel + link-rewrite at `track.meridian.io` is probably 3–5 days. Could be a v1.0 quick win instead of v1.1 roadmap item. Decision point: Phase 3 retrospective.
2. **Trial limits calibration.** 100 opps ingested + 50 sends are first-pass guesses. Measure after first 10 tenants through onboarding; recalibrate before GA.
3. **Salesforce AppExchange listing timing.** v1.1 vs v1.2. Unblocks enterprise distribution but adds months of review cycles. Decision point: after first 10 paying tenants.
4. **Inbound parse provider.** SendGrid Inbound Parse is the v1.0 pick (simplest integration, already Meridian's transactional provider). Evaluate AWS SES inbound + Lambda as alternative if transactional volume grows enough to rehost off SendGrid.
5. **CASA assessor selection — pre-v1.1 kickoff.** Decision deferred to ~6 weeks before v1.1 GA. Candidates: Laika (startup-friendly, ~$15–20K/yr), Schellman (Big-4 adjacent, ~$20–30K/yr, SOC 2 bundleable), Bishop Fox (premium, ~$25–40K/yr).

---

## 10. Glossary

| Term | Definition |
|---|---|
| Tenant | A customer organization using Meridian. |
| Market Profile | Tenant-defined configuration of target market (NAICS, keywords, agency tiers, value thresholds). Replaces v3's "Lane." |
| Source | A configured origin of opportunities. Either a built-in adapter or a tenant-authored generic connector. |
| Source Adapter | Code that implements ingestion for a source type. |
| Source Definition | Tenant-scoped record of an enabled source with its parameters. |
| CRM Connection | Tenant-scoped authenticated link to a CRM with field mappings. |
| Opportunity | A procurement solicitation. |
| Pursue / Partner / NoBid | Scoring-derived outcomes. Thresholds are tenant-configurable. |
| POC | Point of Contact. A real human at an agency or prime contractor. |
| Sequence | Multi-step automated outreach campaign. |
| Enrollment | A specific contact enrolled in a specific sequence for a specific opportunity. |
| Suppression | A contact or domain that must never receive outreach. |
| Audit Log | Append-only tenant-scoped event record. Immutable. |

---

## 11. Appendix — Example Tenants

### 11.1 "Acme Contact Center" (illustrative, based on former KomBea internal scope)

- **Market Profile:** Federal + state contact center services
- **NAICS:** 561422, 541990, 541512
- **Title keywords:** "contact center", "call center", "customer service", "citizen services"
- **Agency tiers:**
  - Tier 1: IRS, SSA, VA, Utah DTS, Virginia ITA
  - Tier 2: other federal cabinet agencies, large state IT depts
  - Tier 3: other state/local
- **Custom scoring rules:**
  - Seat-signal regex: `\b(\d{2,4})\s+(?:seats?|agents?|stations?)\b` → extract integer, +2 if ≥ 100
  - Incumbent recompete: title contains "recompete" or description mentions specific legacy platform → +1
- **CRM:** Pipedrive
- **Email:** Microsoft 365 OAuth, dedicated `outreach@acmecc.com` mailbox

This is recoverable as a tenant config JSON on signup — not code.

### 11.2 "Bravo Federal Services" (illustrative)

- **Market Profile:** Federal IT services
- **NAICS:** 541512, 541519, 541511
- **Title keywords:** "cloud migration", "modernization", "zero trust"
- **Agency tiers:** DoD components as Tier 1, civilian agencies as Tier 2
- **CRM:** Salesforce
- **Email:** Gmail OAuth
