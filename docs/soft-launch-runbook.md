---
title: Soft Launch Runbook (v3.0)
summary: Step-by-step playbook for firing the first Meridian sequence end-to-end against a real opportunity, plus the local dry-run path that proved it works.
keywords: soft launch, runbook, deployment, processing job, sequence engine, dry run
category: Operations
---

# Meridian v3.0 Soft Launch — Runbook

**Spec milestone:** §7.3 Week 3 — *"run first Meridian sequence against a real opportunity."*

This document is the operator's playbook for going live. It assumes the fixes
landed in commits `2ea8a7a` (pipeline rewrite), `a55ce59` (worker wiring),
`df1a111` (integration tests), and the soft-launch readiness commits that
follow.

## What you're firing

The full post-ingest path:

```
Source ingest → Status=New opportunity
  → ProcessingJob (every 30 min)
      → score → POC enrichment → CRM deal create → auto-enroll into sequence
  → SequenceJob (every 2h)
      → render template → send via Resend → record EmailActivity → advance step
```

If anything in this chain isn't configured for the target tenant, the chain
silently no-ops at that stage. The runbook below makes each prerequisite
explicit.

## Verified locally on 2026-04-28

Single-command dry-run against the dev DB:

```bash
dotnet run --project src/Meridian.Worker -- --smoke <tenant-slug>
```

This seeds the outreach scaffold (Console-provider OutboundConfiguration +
one OutreachTemplate + one OutreachSequence) and a dedicated
`DEMO-SOFT-LAUNCH` opportunity at `Status=New`, then runs `ProcessingJob`
and `SequenceJob` once each. Expected log lines on success:

```
Sample opportunities: 1 added
Pipeline processed 1 opportunities for tenant <id>: 1 pursue, 0 partner, 0 no-bid, 1 contacts, 1 deals, 1 enrollments
[EMAIL:<id>] From: noreply@meridian.local <Meridian (dev)> -> jordan+...@example.test | Subject: Re: State of Utah Contact Center Modernization — 200 seats
Sequence job sent 1 emails for <slug>
Smoke run complete for <slug>
```

The dev synthetic enricher (`DevSyntheticPocEnricher`) replaces the real
SAM.gov / USASpending enrichers when the host environment is `Development`,
so the dry-run doesn't depend on real API keys.

## Production prerequisites

Before pointing the worker at production data, the following MUST be true.

### Hosts

- Both Portal and Worker deployed to Burrow.
- Both processes share the SAME Data Protection keyring. Use
  `services.AddDataProtection().PersistKeysToFileSystem(<shared volume>).SetApplicationName("Meridian")`
  on both. Without this, the Worker can't decrypt secrets the Portal wrote.
- `ConnectionStrings:Meridian` is set on both via env var or appsettings
  override. The Worker has no fallback; it'll throw at startup.
- `ASPNETCORE_ENVIRONMENT` is **not** `Development` in production, otherwise
  the dev synthetic enricher overrides the real ones and you'll send fake
  contacts.
- Schema is current. Either let the Portal apply migrations on startup
  (existing behavior — Portal must boot before Worker) or run
  `dotnet ef database update` against the prod connection beforehand.

### Per-tenant configuration (via Portal)

Per the spec, all per-tenant config goes through the Portal. The Portal
already exposes UIs for:

- Tenant signup + invitations ✓
- OIDC SSO admin ✓
- Source definitions (RSS / REST / HTML / Webhook / SAM.gov / MyBidMatch) ✓
- CRM connections (Pipedrive / HubSpot / Salesforce) ✓
- Outbound configuration (Resend API key, From, Compliance footer, DailyCap) ✓

The Portal does **not yet** expose a UI for OutreachSequence or
OutreachTemplate management. For the v3.0 soft launch, sequences and
templates must be seeded via SQL or by extending `DevSeedService` for the
target prod tenant. This is the most important v3.0.x follow-up.

### Pre-launch checklist for the chosen tenant

1. **Tenant exists.** `SELECT id, slug FROM tenants WHERE slug = '<target>';`
2. **Source defined and enabled.** At least one `source_definitions` row
   with `is_enabled = true`. SAM.gov is the safest start.
3. **OutboundConfiguration created via Portal /settings/outbound.**
   - Provider = Resend; API key entered (will be encrypted on save).
   - From / FromName / Reply-To / Physical address / Unsubscribe URL all set.
   - DailyCap set to a small number for the first run (5–10).
4. **OutreachSequence + OutreachTemplate seeded.** No Portal UI yet — see
   `DevSeedService.SeedOutreachScaffoldAsync` for the shape, or insert via
   SQL. AgencyType on the sequence should match the agencies your sources
   ingest, or any sequence will be picked as a fallback.
5. **Resend webhook configured** for bounce + complaint handling. Webhook
   secret stored on the OutboundConfiguration.

### Recommended first run

1. Disable all sources except one low-volume source for the chosen tenant.
2. Start the Worker. It'll run `IngestionJob` at 06:00 UTC (or restart it to
   trigger immediately).
3. Within 30 min, `ProcessingJob` picks up new opportunities, scores them,
   enriches POCs, creates CRM deals, enrolls contacts.
4. Within 2h, `SequenceJob` sends the first email.
5. Watch `Audit Log` (Portal /activity) for `OpportunityScored`,
   `DealCreated`, `EnrollmentCreated`, `EmailSent` events.

### Kill switch

To halt sends without redeploying:

- **Per-tenant pause:** flip `outbound_configurations.is_enabled = false` in
  the DB or via Portal /settings/outbound.
- **Global halt:** stop the Worker host. The Portal continues to function;
  ingested opportunities sit at `Status=New` until the Worker resumes.

## Known gaps (acknowledged for v3.0 → resolve in v3.0.x or v3.1)

| Gap | Impact | Mitigation for v3.0 |
|---|---|---|
| No Portal UI for OutreachSequence / OutreachTemplate | Operator can't create sequences without SQL | Seed via SQL or `DevSeedService` for the first tenant |
| `Opportunity` has no `OpportunityType` field | Sequence selection is AgencyType-only | Use one sequence per AgencyType per tenant |
| No automated migration runner in Worker | Worker assumes Portal has applied migrations | Deploy Portal first, or run `dotnet ef database update` |
| No prod blue-green script per global SUPREME-0 | Manual deploy only | Document the deploy commands in the deploy runbook (separate doc) |
| No health endpoint on Worker | Can't probe Worker liveness from external monitoring | Tail logs; add `/health` in v3.1 |
