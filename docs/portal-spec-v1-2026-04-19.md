# Meridian Portal — UI/UX Specification (v1.0 Product)
**Version:** 1.0 Draft
**Date:** 2026-04-19
**Author:** Tommy Shelburne
**Status:** Drafting — aligned with `spec-product-v1-2026-04-18.md`
**Supersedes:** prior internal-scope draft (now obsolete)

**Stack:** ASP.NET Core 10 + Blazor Server
**Auth:** Local email+password with TOTP 2FA; optional per-tenant OIDC (Entra, Okta, Google Workspace, Auth0)

---

## 1. Design Philosophy

The portal has three jobs, in priority order:

1. **Onboarding.** Make the first signup-to-first-ingestion flow feel like 30 minutes of deliberate work, not 30 minutes of friction. The product's core promise lives here.
2. **Visibility.** See everything Meridian is doing for you — across sources, CRMs, sequences — without opening another tool.
3. **Control.** Intervene at any point: reject an opportunity, pause an enrollment, edit a template, reconfigure a source — without writing code or tickets.

Every page follows the same pattern: **status at a glance → drill down → take action**. No buried settings, no dead ends.

**Company-agnostic from day one.** No hardcoded users, company names, industry assumptions, CRMs, email providers, or target markets. Every tenant-specific dimension is configured by the tenant through this portal.

---

## 2. Navigation Structure

```
Meridian (tenant scope)
├── Dashboard
├── Pipeline
│   ├── Opportunity Queue
│   ├── Active Deals
│   └── Watched Bids
├── Outreach
│   ├── Sequences
│   ├── Enrollments
│   ├── Templates
│   └── Activity Log
├── Contacts
│   ├── Directory
│   └── Enrichment Queue
├── Sources
│   ├── Marketplace
│   └── My Sources
├── Intelligence
│   ├── Market Profiles
│   ├── Scoring Rules
│   └── Memory Browser  (v1.1)
└── Settings
    ├── Organization
    ├── Plan & Usage
    ├── Users & Roles
    ├── Connections         (CRM + Email)
    ├── Send Limits
    ├── Suppression List
    ├── Security            (2FA, OIDC, sessions)
    └── Audit Log
```

Tenant context is resolved from the session and reflected in the header (org name, plan tier badge, user avatar with menu). No tenant switcher at launch — each user is scoped to one tenant. Multi-tenant user scoping is a v1.2 feature.

---

## 3. Public Surface (pre-auth)

| Page | Purpose |
|---|---|
| `/` — Landing | Marketing homepage; CTA to signup. |
| `/signup` | Email + password, org name, agree to ToS/privacy. |
| `/login` | Email + password. Detects OIDC on tenant's domain and offers SSO redirect. |
| `/verify-email/:token` | One-click email verification. |
| `/forgot-password` + `/reset-password/:token` | Reset flow. |
| `/oidc/callback/:tenant` | OIDC redirect target. |
| `/u/:unsubscribe-token` | One-click unsubscribe landing (CAN-SPAM §5.1). Confirms unsubscribe + shows who's sending and why. |

Public pages **must** be mobile-friendly (opened from mobile email clients). Everything else is desktop-first.

---

## 4. Onboarding Wizard

The onboarding wizard is the most important UI surface in v1.0. Target time-to-value: **30 minutes to first ingested opportunity; 2 hours to first real send.**

### 4.1 Flow (7 steps, each resumable)

| # | Step | What the user does | Exit condition |
|---|---|---|---|
| 1 | Welcome | Confirm org name, slug (auto-suggested from email domain, editable), time zone. | Slug validated and tenant provisioned. |
| 2 | Connect CRM | Pick provider tile (Pipedrive / HubSpot / Salesforce) → OAuth redirect → consent → return. Select default pipeline. Auto-maps common fields; flags unmapped for review. | Test write of a dummy deal succeeds; user deletes it. |
| 3 | Connect Email | Pick provider tile (Microsoft 365 OAuth / Google Workspace SMTP / Generic SMTP / SendGrid / Mailgun / Postmark). OAuth or credential capture. Show SPF/DKIM/DMARC status with remediation links. | Test email delivered to the signup address. |
| 4 | Create Market Profile | Pick a starter pack (Federal IT Services / State-Local Contact Center / Professional Services / Construction / Blank). Review NAICS, keywords, agency tiers inline — everything editable. | At least one keyword and one NAICS code present. |
| 5 | Enable Sources | Multi-select built-in adapters, NAICS filter prefilled from Market Profile. Optionally add first generic source. | At least one source enabled. |
| 6 | Templates & Sequence | Import a starter template pack. Pick a sequence preset (5-touch / 3-touch / single-send). Live-render the first template against a sample opportunity. | Sequence saved and at least one template previewed. |
| 7 | Go Live | Summary card of everything configured. Reminder that sending is **dry-run for 24h** (emails rendered and logged but not dispatched). Click "Activate." | Activation recorded; first source run scheduled within 5 minutes. |

**Resumability.** Each step persists on blur. User can leave and return; wizard resumes at the last incomplete step. Skipping optional sub-choices is allowed; skipping a whole step is not.

**Safety rails surfaced in the UI:**
- Step 7 shows the 24h dry-run switch prominently with a clear "what this means" explanation.
- Daily cap is preset to 20 sends/day regardless of configured target. Warm-up mode is on by default.
- No sequence can be activated unless the first template has been previewed at least once.

### 4.2 Post-Onboarding

After activation:
- Banner on Dashboard: "You're in dry-run until {timestamp}. [Go live now]" — one-click override for tenants who know what they're doing.
- Email to signup address confirming setup with links to Sources, Sequences, and the Activity Log.
- If any step produced warnings (e.g., SPF misconfigured, field mapping incomplete), a "Setup checklist" card appears on Dashboard until resolved.

---

## 5. Pages

### 5.1 Dashboard

First screen every morning. No scroll needed for the full picture on a standard laptop.

**KPI strip (today vs. 7-day avg):** Opportunities ingested · Emails sent · Replies received · Meetings booked · Enrichment rate.

**Pipeline funnel (clickable stages):**
```
Ingested → Scored → Pursue/Partner → Enrolled → Replied → Meeting
```

**Source health card:** Grid of enabled sources with last-run timestamp, last-run status, and a sparkline of ingest volume over the last 14 days. Red outline on any source that's failed > N times. Click → jumps to My Sources with that source selected.

**Activity feed (right panel):** Live-scrolling system events, actor-labeled, timestamped. Auto-refreshes every 30s via SignalR.

**Alerts (dismissable, top):** Opportunities with response deadline < 5 days and no POC enrolled · Enrollments stalled · Bounced contacts with no replacement · Plan usage approaching limits (80% and 95% thresholds) · Source auto-disabled due to failures.

**Plan usage widget:** Opportunities ingested this month, emails sent this month, against plan cap. Starter/Pro/Enterprise tier badge with "Upgrade" CTA if >80% utilized.

---

### 5.2 Pipeline

**Opportunity Queue.** New scored opportunities pending review.

- Filters: source, score range, agency tier, state/jurisdiction, value range, posted date, status (Pending / Approved / Rejected / Watching)
- Table: title + agency, score badge (Pursue/Partner/NoBid color per tenant's thresholds), estimated value, source (badge includes provider icon for generic adapters), response deadline (red if < 5 days), POC status, quick actions
- Detail drawer: full description, score breakdown per scoring rule (each rule with points awarded + reasoning), matched contacts with confidence scores, manual contact entry, sequence selector, primary action (Enroll / Send to CRM only)

**Active Deals.** Kanban mirror of the tenant's CRM pipeline stages (fetched via `ICrmAdapter.ListPipelinesAsync` at load).

- Stages dynamic per CRM — Pipedrive/HubSpot/Salesforce each have their own default stages plus tenant-custom stages
- Card shows: agency + title, current sequence step, days since last touch, next scheduled send
- Card drawer: full outreach timeline (emails sent, replies, CRM activities), rendered email bodies at send time, contact details, manual actions (pause, send next now, mark replied, log call, close)

**Watched Bids.** Tracking mode — amendments and deadlines, no active outreach. Action: promote to Opportunity Queue.

---

### 5.3 Outreach

**Sequences.** List with name, opportunity type, agency type, step count, active enrollment count, last-used. Editor:
- Visual step timeline (Step 1 → Day 0, Step 2 → Day N, …)
- Per-step: delay days, send window start/end, jitter minutes, template assignment, subject line preview
- Live token preview — paste a real contact + opportunity from the DB, see exactly what will render
- Version history; rollback available

**Enrollments.** Every active/paused/completed/stopped thread in one table.
- Filters: status, sequence, agency, enrolled date, next-send date
- Row actions: Pause / Resume / Skip to next / Stop / View thread
- Bulk actions: Pause all for agency (use case: live phone call in progress), resume batch after conference/travel

**Templates.** Split-pane editor — Liquid source left, live-rendered preview right. Token reference sidebar with all available `{{tokens}}`. "Send test" button renders with a real contact + opportunity and delivers a preview to the current user's email. Version history.

**Activity Log.** Immutable tenant-scoped record of everything Meridian has ever done.
- Filters: event type, entity type, actor (system vs. user), date range
- Row expand: full JSON payload
- CSV export
- **No delete, no edit, no archive, no hide.** Any UI affordance that would violate this must not exist.

---

### 5.4 Contacts

**Directory.** Every POC across all sources.
- Columns: name, title, agency, email, phone, source, confidence, enrichment date, opt-out status, active enrollments
- Filters: agency, source, confidence range, opted out, bounced, has active enrollment
- Detail: all enrichment sources with individual confidence, full outreach history, RAG memory (v1.1), manual edit, opt-out button (immediate, permanent, irreversible)

**Enrichment Queue.** Opportunities where automated POC search returned nothing. Manual-input row action: Assign contact (search existing or create new). On assignment, opportunity flows back to Opportunity Queue.

---

### 5.5 Sources (new in v1.0)

**Marketplace.** Gallery of available adapters, categorized.

| Category | Contents |
|---|---|
| Federal | SAM.gov, USASpending.gov, GSA eBuy |
| State | CA, TX, FL, NY, IL, VA, GA, NC, WA, CO, UT (more coming) |
| Generic (tenant-configured) | RSS/Atom Feed, REST API, Inbound Webhook, Email-to-Ingest, CSV Upload |

Each tile: provider icon, name, short description, setup time estimate, Enable/Create CTA, badge ("Built-in" / "Custom" / "Coming soon").

Filter + search bar. Tiles gated by plan tier show a lock badge and "Upgrade to unlock."

**Per-adapter Enable flow (built-in):** Inline config drawer with:
- NAICS filter (prefilled from active Market Profile; editable)
- Keyword filter (same)
- Polling cadence (default per adapter; editable)
- Any adapter-specific parameters (credentials, category filters, etc.)
- **Test run** button — pulls N results without persisting, shows a preview table

**Per-adapter Create flow (generic):** Wizard, one per adapter type.

- **RSS:** Feed URL → preview first 5 items → drag-to-map RSS fields to opportunity fields → save.
- **REST API:** Endpoint, method, auth config (None/API Key/Bearer/Basic/OAuth Client Credentials), request template, "Test" button showing raw response → JSONPath mapper for response fields → pagination config → polling cadence.
- **Inbound Webhook:** Save first → Meridian auto-generates unique URL + signing secret → shows curl example with `X-Meridian-Signature` header → JSON payload field mapper → optional rate-limit override (plan-capped).
- **Email-to-Ingest:** Save first → Meridian auto-generates unique address (`ingest+{slug}-{id}@meridian.io`) → optional sender-domain allowlist → regex-based field extractor (example email paste to test) → quarantine settings.
- **CSV Upload:** Upload file → column mapper → one-shot or scheduled re-upload (tenant provides a URL Meridian polls).

**My Sources.** List of the tenant's enabled sources.

- Columns: name, type, status (Enabled / Disabled / Auto-Disabled / Error), last run, ingest volume (7d), error rate (7d), actions
- Per-source detail: run history (last 50 runs with status + counts), error log, edit configuration, disable/enable, delete
- **Auto-disable indicator** prominent — if Meridian paused a source for repeated failures, it says so with the reason and a "Re-enable after fix" button

---

### 5.6 Intelligence

**Market Profiles.** Tenants can have multiple profiles (max depends on plan — Starter 1, Pro 3).

- List: name, NAICS count, keyword count, active (toggle), last-edited
- Editor: name, NAICS list (searchable picker), title keywords, description keywords, agency tiers (drag-reorder tier ranks, add/remove agencies per tier), value thresholds, active toggle
- Clone button — duplicate an existing profile as a starting point
- Import starter pack — one-click overlay of a preset profile

**Scoring Rules.** Rule-based scoring builder (replaces old "Scoring Config" with hardcoded dimensions).

- Rules table: drag-to-reorder, enable/disable toggle per rule, name, rule type, points, config summary
- Add rule → type picker (KeywordMatch / NaicsMatch / AgencyTierMatch / ValueThreshold / VehicleMatch / RegexMatch / CustomExpression [v1.1])
- Per-rule editor specific to type — e.g., KeywordMatch: field selector, keyword list, points-per-match, cap
- **A/B comparison view:** "Score last week's opportunities with current rules vs. proposed rules." Shows distribution delta before the tenant commits.
- Thresholds (Pursue / Partner / NoBid) configurable, live-previewed against the last 30 days of scored opportunities

**Memory Browser.** v1.1 — shell exists in v1.0 but inactive. Shows "pgvector RAG available in v1.1."

---

### 5.7 Settings

**Organization.** Org name, slug (immutable after provisioning — tied to unique URLs and ingest addresses), time zone, logo (v1.2 branding), physical mailing address (required, injected into email footers for CAN-SPAM).

**Plan & Usage.** Current plan tier + limits. Usage bars for opportunities ingested, emails sent, custom-source inbound events — this month. Upgrade CTA → Stripe-hosted checkout when billing lands in v1.2; "Contact sales" button until then.

**Users & Roles.** Invite users by email, assign role (Owner / Admin / Operator / Viewer). Revoke access. Pending-invite state. Role permissions tooltip per role. Plan-tier cap on user count.

**Connections.** Grid of connection cards, two sections:

1. **CRM** — current connection card (provider logo, connected-as account, last sync, pipeline selection, field mappings). Actions: Reconfigure fields, Disconnect, Reconnect. "Add CRM" button — portal supports multiple CRM connections in v1.1; v1.0 = one per tenant.
2. **Email** — current connection card (provider, from-address, from-display-name, last test-send, daily-cap usage bar, SPF/DKIM/DMARC status indicators). Actions: Reconfigure, Send test, Disconnect, Switch provider. Reply-threading mode shown ("Native" for Graph OAuth, "Reply-proxy via `reply.meridian.io`" for all others) with inline explanation.

**Send Limits.** Daily global cap, per-domain cap, send window start/end (tenant time zone), jitter minutes, warm-up mode toggle + current warm-up day counter.

**Suppression List.** View, search, manual add (domain or individual address). CSV import for bulk suppression. Shows reason + date added. Permanent — no bulk delete. CAN-SPAM-protected.

**Security.** 2FA enforcement (required on Starter+), OIDC configuration (issuer URL, client ID, client secret, attribute mapping), active sessions list with revoke, password policy, API tokens (v1.1), audit-log viewer link.

**Audit Log.** Same as Outreach → Activity Log but scoped to setting changes — who changed scoring rules, who updated send limits, who invited users, who connected a new source. Immutable, CSV-exportable.

---

## 6. Reusable Components

These patterns appear across many pages. Spec them once, use them everywhere.

**Connection Card.** Visual anchor for any integration (CRM, Email, Source). Always shows: provider logo, name, connected-as identity, status indicator, last-activity timestamp, action menu. Consistent across Settings → Connections and My Sources.

**Config Drawer.** Slide-in right-side drawer for any inline edit (opportunity detail, source config, template preview, enrollment detail). Never navigates away from the list. Dismissable. Unsaved-changes confirmation on close.

**Wizard Frame.** Multi-step flows (onboarding, connect CRM, connect email, create generic source). Consistent header with step indicator, back/next/save-and-continue-later, keyboard navigable.

**Field Mapper.** Two-column drag mapper for connecting Meridian fields to external-system fields. Used in CRM field mapping, generic REST response mapping, RSS mapping, CSV column mapping. Same component with provider-specific field schemas.

**Confirm Destructive.** Modal with specific-consequence text, not "Are you sure?" — e.g., "This will stop all future emails to John Smith and suppress the address permanently. This cannot be undone." Required before any destructive action.

**Empty State.** Never a blank table. Every empty state explains what it means (operational vs. unconfigured vs. error) and offers the relevant next action.

**Score Badge.** Pursue (green) / Partner (yellow) / NoBid (grey) using tenant-configured thresholds. Same component wherever a score appears — queue, kanban card, contact history, activity log, drawer.

**Source Health Indicator.** Green (last run succeeded, no backlog), yellow (warnings or stale beyond expected cadence), red (auto-disabled or repeated failures). Same semantics on Dashboard, Sources list, and every source reference.

---

## 7. UX Rules for Build Sessions

Stated explicitly at the start of every portal build session so they carry across context windows.

1. **No full-page reloads for actions.** Everything that doesn't require navigation uses a drawer, modal, or in-place Blazor component update. Blazor Server + SignalR handles this natively.
2. **Every destructive action requires a specific-consequence confirmation.** Pause, reject, delete, opt-out, disconnect — confirm text describes the consequence. Never just "Are you sure?"
3. **Status is always fresh.** Dashboard KPIs and activity feed auto-refresh every 30s via SignalR. Other pages show "last updated" with manual refresh.
4. **Mobile-readable, not mobile-first** — except public pages (signup, login, unsubscribe) which must be usable on mobile.
5. **Empty states are informative.** Never a blank table.
6. **Audit log is sacred.** Read-only, CSV-exportable, retained indefinitely. No UI path to modify audit records.
7. **Score badges are consistent.** Same green/yellow/grey system everywhere. Thresholds are tenant-configurable but the semantic colors are not.
8. **Template + sequence previews render exactly what will be sent.** Same `LiquidTemplateRenderer` as the worker. If the preview looks wrong, the email will be wrong.
9. **Auth gates everything post-signup.** No unauthenticated routes except the public surface in §3. Session expiry redirects to login, never a broken page.
10. **Settings changes are logged.** Every mutation to scoring rules, market profiles, send limits, suppression list, connections, users — written to the audit log with actor identity.
11. **Plan-tier gating is visible, not hidden.** Features above the tenant's plan show a lock badge with "Upgrade" context. No silent failures.
12. **Onboarding progress persists.** A user closing the tab mid-wizard returns to the same step. Each step saves on blur.
13. **Dry-run mode is visually distinct.** When a tenant is in the 24h post-activation dry-run (or has toggled dry-run back on), every send-related surface shows a clear banner: "Dry-run — emails are rendered and logged but not sent." Banner color is orange, not red (dry-run is normal, not an error).
14. **Source health surfaces prominently.** Failures in ingestion are the most common tenant-visible problem. Dashboard source-health card is always above the fold. Per-source error causes and remediation steps are one click away.
15. **Never show another tenant's data.** EF Core global query filters enforce tenant isolation at the data layer. The portal assumes they work but never depends on that assumption alone — integration tests include cross-tenant leak checks.

---

## 8. Roles & Permissions

| Action | Owner | Admin | Operator | Viewer |
|---|---|---|---|---|
| Billing, plan changes, org deletion | ✓ | | | |
| Invite/revoke users, change roles | ✓ | | | |
| Configure Connections (CRM, Email) | ✓ | ✓ | | |
| Configure Sources, Market Profiles, Scoring Rules | ✓ | ✓ | | |
| Edit Sequences, Templates | ✓ | ✓ | ✓ | |
| Approve/reject opportunities, enroll contacts | ✓ | ✓ | ✓ | |
| Pause/resume enrollments, log calls | ✓ | ✓ | ✓ | |
| View Dashboard, Pipeline, Contacts, Activity Log | ✓ | ✓ | ✓ | ✓ |

UI hides actions the current role cannot perform rather than showing a disabled state with a tooltip — keeps the interface clean for Operators and Viewers.

---

## 9. Build Order (maps to product spec §7.3)

| Phase | Portal Work |
|---|---|
| Phase 1 (Weeks 1–2) | Auth: signup, login, 2FA, OIDC config stub, email verification, password reset. Users & Roles. Organization settings. Session handling. Blazor app shell with navigation. |
| Phase 2 (Weeks 3–4) | Sources: Marketplace gallery, per-adapter Enable drawers for SAM.gov/USASpending, first three generic-adapter wizards (RSS, REST, Inbound Webhook). My Sources list + detail. Source health card on Dashboard. |
| Phase 3 (Weeks 5–6) | Connect CRM wizard + field mapper for Pipedrive/HubSpot/Salesforce. Connect Email wizard for Graph OAuth + SMTP + SendGrid/Mailgun/Postmark, including Google Workspace SMTP app-password walkthrough. Connections settings page. |
| Phase 4 (Week 7) | Market Profiles editor, Scoring Rules builder with A/B comparison, starter-pack apply. |
| Phase 5 (Week 8) | Onboarding wizard end-to-end (glues Phases 1–4). Dashboard. Opportunity Queue. Active Deals kanban. Sequences, Enrollments, Templates, Activity Log. Plan & Usage widget. Safety rails (dry-run banner, 20/day warm-up). |
| Phase 6 (Week 9) | Suppression List, Send Limits, Security page, Audit Log view. Unsubscribe landing (`/u/:token`). Polish pass on empty states, error states, plan-tier gating visual treatments. |

---

## 10. Open Portal Questions

1. **Onboarding wizard framework.** Blazor multi-step flow vs. a dedicated route per step. Recommendation: dedicated routes (`/onboarding/connect-crm`, etc.) — resumability is cleaner, URL reflects progress, browser back/forward works naturally.
2. **Pipeline kanban performance.** With hundreds of active deals, naive Blazor rendering may stutter. Decision point: virtualize columns in Phase 5 if initial benchmarks justify.
3. **A/B scoring-rule comparison scope.** How far back do we replay — 30 days, 90 days, tenant choice? Defaulting to 30 days keeps compute cheap.
4. **Source marketplace categorization.** Current split (Federal / State / Generic) is clean. Once state count grows past 20, consider grouping by region.
5. **Inline onboarding vs. wizard-first.** Do we force the wizard on first login, or allow "skip and explore empty dashboard"? Recommendation: force it — the empty dashboard is not an informative product state.
