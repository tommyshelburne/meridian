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
- Outbound configuration (Resend API key, From, Compliance footer, DailyCap, Inbound domain) ✓
- OutreachSequence + OutreachTemplate management ✓ (added 2026-04-28, commit `b4bc1ea`)
- Postmark inbound + Resend webhook for replies and bounces — see "Inbound webhooks" below.

### Pre-launch checklist for the chosen tenant

1. **Tenant exists.** `SELECT id, slug FROM tenants WHERE slug = '<target>';`
2. **Source defined and enabled.** At least one `source_definitions` row
   with `is_enabled = true`. SAM.gov is the safest start.
3. **OutboundConfiguration created via Portal /settings/outbound.**
   - Provider = Resend; API key entered (will be encrypted on save).
   - From / FromName / Reply-To / Physical address / Unsubscribe URL all set.
   - DailyCap set to a small number for the first run (5–10).
4. **OutreachSequence + OutreachTemplate seeded** via Portal /settings/sequences.
   - Create at least one template with the email body (Liquid tokens like
     `{{contact.first_name}}`, `{{opportunity.title}}`, `{{agency.name}}`).
   - Create at least one sequence with steps referencing the template(s).
   - AgencyType on the sequence should match the agencies your sources
     ingest; otherwise the first sequence is used as a fallback.
5. **Resend webhook configured** for bounce + complaint handling — see
   "Inbound webhooks → Resend (bounces)" below.
6. **Postmark Inbound configured** for reply routing — see
   "Inbound webhooks → Postmark (replies)" below. Without this, replies
   are silently dropped at the inbound endpoint.

### Recommended first run

1. Disable all sources except one low-volume source for the chosen tenant.
2. Start the Worker. It'll run `IngestionJob` at 06:00 UTC (or restart it to
   trigger immediately).
3. Within 30 min, `ProcessingJob` picks up new opportunities, scores them,
   enriches POCs, creates CRM deals, enrolls contacts.
4. Within 2h, `SequenceJob` sends the first email.
5. Watch `Audit Log` (Portal /activity) for `OpportunityScored`,
   `DealCreated`, `EnrollmentCreated`, `EmailSent` events.

## Inbound webhooks

Two webhooks land in the Portal. Both are required for v3.0 reply +
bounce handling. Both must be set up *per tenant* before the first send.

### Postmark (replies)

Postmark Inbound parses incoming mail and POSTs it to Meridian. Replies
route to the correct tenant by **mailbox hash = tenant slug**.

**Outbound side (Slice 1 of v3.0.x).** Set **Inbound domain** in Portal
/settings/outbound to the same domain you'll verify in Postmark
(e.g. `reply.meridian.app`). Meridian composes per-send Reply-To as
`replies+<tenant-slug>@<inbound-domain>`. The slug is what Postmark
extracts into `MailboxHash` on the inbound webhook payload.

**Postmark side.**

1. Add an Inbound server in Postmark; verify the same domain you set
   above (DNS records: MX → `inbound.postmarkapp.com` per Postmark docs,
   plus SPF/DKIM as Postmark instructs).
2. Set the inbound stream's **Webhook URL** to:
   `https://<portal-host>/api/webhooks/postmark/inbound`
3. Postmark inbound webhooks support Basic Auth only. In Postmark, set
   a username + password under the server's inbound webhook settings.
4. Mirror the same credentials in Meridian's `appsettings.json` (or
   environment variables) on **both Portal and Worker hosts**:
   ```json
   {
     "PostmarkInbound": {
       "Username": "<chosen-username>",
       "Password": "<chosen-password>"
     }
   }
   ```
   If either value is blank, the endpoint returns `503 Service
   Unavailable` and logs `Postmark inbound webhook hit but credentials
   not configured.`

**Smoke test.** From a real inbox, reply to a `--smoke` send. Within a
few seconds:

- Postmark "Activity" tab shows the reply received + the webhook
  delivery attempt with a `202 Accepted`.
- Portal `/app/<slug>/replies` shows the new reply row (or, if it's
  an auto-reply, see "OOO suppression" below).
- Portal `/app/<slug>/activity` shows a `ReplyReceived` (or
  `AutoReplyDetected`) event.

**Common failures.**

| Symptom | Cause |
|---|---|
| Postmark shows 401 Unauthorized | Basic Auth creds in appsettings ≠ Postmark's webhook creds |
| Postmark shows 503 | `PostmarkInbound:Username/Password` blank in appsettings |
| Postmark shows 202 but Portal /replies stays empty | `MailboxHash` is empty on the payload — Reply-To wasn't composed with `+<slug>`. Check Portal /settings/outbound: is "Inbound domain" set? |
| Wrong tenant gets the reply | Outbound Reply-To embedded the wrong slug — check the EmailActivity row, recompose if InboundDomain was wrong |

### Resend (bounces + complaints)

Resend posts bounce / complaint events to Meridian as Svix-signed
webhooks. Without this, hard-bounced contacts stay "active" forever
and you'll keep sending to dead addresses.

**Steps.**

1. Portal /settings/outbound → **Resend webhook signing secret** field.
   Paste the secret from Resend's webhook setup (Resend gives you a
   `whsec_…` string). Meridian encrypts it with DataProtection before
   storing.
2. In Resend's dashboard, point the webhook at:
   `https://<portal-host>/api/webhooks/resend/<tenant-id>`
   The `<tenant-id>` is the **GUID**, not the slug. Find it in Portal
   /settings or via `SELECT id FROM tenants WHERE slug = '<slug>';`.
3. Subscribe at least to `email.bounced` and `email.complained`.

**Smoke test.** Send to a known-bad mailbox (Resend supports
`bounced@resend.dev` for hard bounces; `complained@resend.dev` for
complaints).

- Resend "Events" shows the delivery + the webhook attempt with a
  `202 Accepted`.
- Portal /activity shows `EmailBounced` or `EmailComplained`.
- The contact's `IsBounced` flag flips true; future sequence steps
  skip them.

**Common failures.**

| Symptom | Cause |
|---|---|
| Resend shows 404 | `EncryptedWebhookSecret` not set on the OutboundConfiguration — fill in /settings/outbound first |
| Resend shows 401 Unauthorized | Signing secret in /settings/outbound ≠ Resend's webhook secret |
| Resend shows 500 | DataProtection key not shared between Portal + Worker, or keyring rotated — re-paste the secret |
| Webhook OK but contact still flagged active | Event payload type isn't one of the handled set — parser returns 202 (no-op) on unknown types; check the body in Resend's event detail |

### OOO suppression (shipped 2026-05-12, commit `603cd50`)

Out-of-office and other auto-replies are detected at the Postmark inbound
parser and suppressed. Without this, every vacation auto-reply marks
the prospect as `Replied` and halts the sequence prematurely.

**What's detected** (any one of these triggers OOO classification):

- RFC 3834 `Auto-Submitted` header is anything other than `no`
- `X-Autoreply` or `X-Autorespond` header is present
- `Precedence` header is `auto_reply`, `auto-reply`, `bulk`, `list`,
  or `junk`
- Subject (anchored at start) matches: `Out of Office`,
  `Automatic Reply`, `Auto-Reply`, `Away from my desk`.
  Anchored-at-start so `Re: Out of Office last week` from a real human
  is NOT suppressed.

**Behavior on match:**

- Activity audit log gets an `AutoReplyDetected` event (neutral badge
  in /activity, filterable from the event-type dropdown).
- The reply is NOT recorded against the contact; sequence keeps going.
- The reply does NOT appear in /replies — only in /activity under the
  `AutoReplyDetected` filter. **This is a known transparency gap**;
  Slice 4 of the v3.0.x unblocker plan surfaces suppressed replies in
  /replies behind a "Show suppressed" toggle.

**Spotting false positives during soft launch.**

- Filter /activity to `AutoReplyDetected` and skim recent events.
- If you see a hit that's clearly a real reply (e.g. the body contains
  "interested" + a calendar link), it's a false positive: open the
  EmailActivity record, copy the body for analysis, and consider
  pausing the sequence manually until Slice 4 ships.

### Kill switch

To halt sends without redeploying:

- **Per-tenant pause:** flip `outbound_configurations.is_enabled = false` in
  the DB or via Portal /settings/outbound.
- **Global halt:** stop the Worker host. The Portal continues to function;
  ingested opportunities sit at `Status=New` until the Worker resumes.

## Known gaps (acknowledged for v3.0 → resolve in v3.0.x or v3.1)

| Gap | Impact | Mitigation for v3.0 |
|---|---|---|
| ~~No Portal UI for OutreachSequence / OutreachTemplate~~ | Resolved 2026-04-28 (commit `b4bc1ea`) — Portal /settings/sequences exposes list + create for both | — |
| ~~Reply-To not auto-routed to tenant mailbox-hash~~ | Resolved 2026-05-13 (Slice 1) — set "Inbound domain" in /settings/outbound; replies route per-tenant | — |
| OOO false positives are invisible in /replies | Operator can only spot them in /activity under `AutoReplyDetected` filter | Slice 4 will surface them in /replies with "Show suppressed" toggle |
| `Opportunity` has no `OpportunityType` field | Sequence selection is AgencyType-only | Use one sequence per AgencyType per tenant |
| No automated migration runner in Worker | Worker assumes Portal has applied migrations | Deploy Portal first, or run `dotnet ef database update` |
| No prod blue-green script per global SUPREME-0 | Manual deploy only | Document the deploy commands in the deploy runbook (separate doc) |
| No health endpoint on Worker | Can't probe Worker liveness from external monitoring | Tail logs; Slice 3 adds `/health` |
