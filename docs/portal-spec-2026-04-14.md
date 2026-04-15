# Meridian Portal — UI/UX Specification
**Version:** 1.0  
**Date:** 2026-04-14  
**Author:** Tommy Shelburne (KomBea) + Claw  
**Status:** Approved for build  
**Stack:** ASP.NET Core + Blazor Server  
**Auth:** Microsoft Entra ID SSO (OIDC)  
**Initial users:** Tommy Shelburne, Angel Shelburne, Art Coombs

---

## Design Philosophy

The portal has two jobs:

1. **Visibility** — see everything Meridian is doing without opening Pipedrive, your inbox, or a terminal
2. **Control** — intervene at any point in the pipeline without writing code

Every page follows the same pattern: **status at a glance → drill down → take action**. No buried settings, no dead ends.

---

## Navigation Structure

```
Meridian
├── Dashboard
├── Pipeline
│   ├── Opportunity Queue
│   ├── Active Deals
│   └── Watched Bids
├── Outreach
│   ├── Sequences
│   ├── Enrollments
│   └── Activity Log
├── Contacts
│   ├── Directory
│   └── Enrichment Queue
├── Intelligence
│   ├── Scoring Config
│   ├── Templates
│   └── Memory Browser
└── Settings
    ├── Lane Config
    ├── Send Limits
    ├── Suppression List
    └── Integrations
```

---

## Pages

### Dashboard

The first thing you see every morning. No scrolling required to get the full picture.

**Top strip — KPIs (today vs. 7-day average):**
- Opportunities ingested
- Emails sent
- Replies received
- Meetings booked
- Enrichment rate (% of Pursue/Partner opps with a verified POC)

**Pipeline funnel (center, visual):**
```
Ingested → Scored → Pursue/Partner → Enrolled → Replied → Meeting
```
Each stage is clickable — jumps to the relevant filtered view.

**Today's activity feed (right panel):**
Live scroll of system events, timestamped, actor labeled (system vs. human). Examples:
- "Scored 4 new opportunities from SAM.gov"
- "Sent follow-up #2 to John Smith at Utah DTS"
- "Reply detected — VA Contact Center RFI"

Auto-refreshes every 30 seconds via SignalR. No manual refresh needed.

**Alerts (top of page, dismissable):**
- Opportunities with response deadline < 5 days and no POC enrolled
- Enrollments stalled (no send in 48h, not paused)
- Bounced contacts with no replacement POC
- Service account sending approaching daily cap

---

### Opportunity Queue

Where new scored opportunities land for human review before outreach begins.

**Filters:** Source, score range, agency type, state, estimated seats, date posted, status (Pending Review / Approved / Rejected / Watching)

**Table columns:**
- Title + agency
- Score badge (color-coded: green = Pursue, yellow = Partner, grey = NoBid)
- Estimated seats
- Source (SAM.gov, Utah U3P, Virginia eVA, etc.)
- Response deadline (red if < 5 days)
- POC status (Enriched / Pending / None)
- Quick actions: **Approve → Enroll**, **Watch**, **Reject**, **View Details**

**Detail panel (slide-in drawer, no page navigation):**
- Full opportunity description text
- Score breakdown — each dimension with points earned and reasoning
- Recompete signal (flagged / not flagged)
- Seat count estimate with confidence level (High / Medium / Low / Unknown)
- Matched contacts with individual confidence scores
- Manual contact entry field if automated enrichment came up empty
- Sequence selector — choose which outreach sequence to enroll the contact in
- Primary actions: **Enroll in sequence** or **Send to Pipedrive only**

---

### Active Deals

Mirror of the Pipedrive pipeline, enriched with Meridian data Pipedrive doesn't have.

**Kanban columns:**
`Contacted → Followed Up → Replied → Meeting Scheduled → Closed Won / Closed Lost`

**Each card shows:**
- Agency name + opportunity title
- Current sequence step (e.g., "Step 2 of 4")
- Days since last touch
- Next scheduled send (or "Waiting for reply")
- Seat count estimate badge

**Card click → deal drawer:**
- Full outreach timeline — every email sent, reply received, Pipedrive activity, in chronological order
- Rendered email bodies — what was actually sent, not just a template name
- Contact details + enrichment source
- Manual actions: Pause enrollment, Send next step now, Mark replied, Log a call, Close deal

---

### Watched Bids

Opportunities being monitored for amendments or deadline changes. Tracking mode only — not in active outreach.

**Table columns:** Title, agency, deadline, days until deadline, last amendment date, amendment count, watch status

**Action:** Move to Opportunity Queue when ready to pursue.

---

### Sequences

The outreach playbook. View, create, and edit sequences without touching the database or code.

**Sequence list columns:** Name, opportunity type, agency type, step count, active enrollments, last used

**Sequence editor:**
- Visual step timeline (Step 1 → Day 0, Step 2 → Day 3, Step 3 → Day 5, etc.)
- Each step: delay in days, send window start/end, jitter minutes, template assignment, subject line preview
- Live token preview — paste a real contact + opportunity and see exactly what the email will look like before it ever sends
- Version history — every edit is versioned, rollback available at any time

---

### Enrollments

Every active, paused, completed, or stopped outreach thread in one table.

**Filters:** Status (Active / Paused / Replied / Bounced / Completed), sequence name, agency, date enrolled, next send date

**Table columns:** Contact name + agency, opportunity title, sequence name, current step / total steps, status badge, next scheduled send, last activity

**Row actions:** Pause / Resume, Skip to next step, Stop enrollment, View full thread

**Bulk actions:** Pause all enrollments for a given agency (useful when a live call is in progress), resume batch after a conference or travel period

---

### Activity Log

Immutable record of everything Meridian has ever done.

**Filters:** Event type (EmailSent, OpportunityScored, DealCreated, ReplyDetected, EnrollmentStopped, etc.), entity type, actor (system / user), date range

**Table columns:** Timestamp, event type, entity (linkable to the relevant record), actor, summary text

**Row expand:** Full JSON payload for each event — useful for debugging sequence or scoring behavior

**Rules:**
- Not editable, not deletable, not hideable
- CSV export available
- Retained indefinitely

---

### Contact Directory

Every POC Meridian knows about, across all sources.

**Table columns:** Name, title, agency, email, phone, source, confidence score, enrichment date, opt-out status, active enrollment count

**Filters:** Agency, source, confidence range, opted out, bounced, has active enrollment

**Contact detail page:**
- All enrichment sources listed with individual confidence scores
- Full outreach history across all opportunities this contact has been associated with
- RAG memory entries (what Meridian "knows" about this person — visible in v3.1)
- Manual edit: update email, add LinkedIn URL, mark verified
- Opt-out button — immediate and permanent suppression, cannot be reversed by re-enrollment

---

### Enrichment Queue

Opportunities where automated POC search returned no results. Requires human input.

**Table columns:** Opportunity title, agency, score, response deadline, days in queue

**Action per row:** Assign a contact — search existing contacts by name/agency or create a new one. Once a contact is assigned, the opportunity automatically moves to the Opportunity Queue for enrollment review.

This is the only workflow that requires manual input. All other pipeline stages are autonomous.

---

### Scoring Config

Tune the scoring engine without a code deploy or restart.

**Configurable parameters:**
- Lane keywords — separate lists for title match (2 pts) and description-only match (1 pt)
- Agency tier table — add, remove, or re-tier any agency
- Scoring dimension weights — adjust relative importance per dimension
- Seat count thresholds — what seat count qualifies for each point value
- Recompete detection keywords
- Known competitor/incumbent list (triggers the recompete scoring bonus)

**Version control:**
- Every config change is versioned with timestamp and actor
- A/B comparison view — shows how the new config would have scored last week's opportunities vs. the previous config before committing the change

---

### Templates

Edit outreach email templates in the browser. No file system access required.

**Template list:** Name, usage count (sent), last modified, associated sequences

**Template editor:**
- Split-pane: Liquid source on left, rendered preview on right (live updates as you type)
- Token reference sidebar — all available `{{tokens}}` with descriptions and example values
- Send test — render with a real contact + opportunity from your database and deliver a preview to your own inbox before any sequence uses it

**Versioning:** Every saved version is retained. Roll back to any prior version.

---

### Memory Browser *(v3.1 — build shell in v3.0)*

Visibility into what pgvector has learned. Not used in normal operations — essential for debugging adaptive tone behavior.

- Free-text search across all memory chunks
- Filter by entity type (Contact, Agency, Opportunity) or specific entity
- Results shown with cosine similarity scores
- Delete individual memory chunks (for cleanup after bad data)
- Bulk re-embed trigger (after major template or scoring changes)

---

### Settings

#### Lane Config
- Active NAICS codes
- Lane keywords (synced with Scoring Config)
- Per-source on/off toggles — enable or disable any ingestion source without a restart

#### Send Limits
- Daily send cap (global and per-domain)
- Send window start and end times (UTC)
- Jitter minutes (randomization within window)
- Warm-up mode toggle — when enabled, daily cap increases automatically by 10 sends/day until target cap is reached

#### Suppression List
- View, search, and manually add suppressed domains or individual addresses
- CSV import for bulk suppression
- Shows suppression reason (opted out, bounced, manual) and date added
- Permanent — no bulk delete

#### Integrations
- **Pipedrive:** Connection status, last successful sync, token rotation button
- **MS Graph service account:** Last successful send, client secret expiry date, renewal reminder date
- **OpenAI embeddings:** Last embedding job status, token usage (last 30 days)
- **PostgreSQL / pgvector:** Connection status, table row counts, last migration applied

---

## UX Rules for Memento Build Sessions

Provide these constraints explicitly at the start of every build session:

1. **No full page reloads for actions.** Everything that doesn't require navigation uses a drawer, modal, or in-place Blazor component update. Blazor Server + SignalR handles this natively — use it.

2. **Every destructive action requires confirmation.** Pause enrollment, reject opportunity, delete template, opt-out contact — confirm dialogs before execution, not instant. Confirm text must describe the consequence specifically (e.g., "This will stop all future emails to John Smith. This cannot be undone." — not just "Are you sure?").

3. **Status is always fresh.** Dashboard KPIs and activity feed auto-refresh every 30 seconds via SignalR. No manual refresh button needed on the dashboard. Other pages show a "last updated" timestamp with a manual refresh option.

4. **Mobile-readable, not mobile-first.** Primary use is desktop (MacBook). Layout must not break on a tablet. Do not over-engineer mobile breakpoints.

5. **Empty states are informative.** Never show a blank table. Empty enrichment queue → "No opportunities awaiting enrichment. System is fully operational." Empty activity log → "No activity recorded yet." Every empty state tells the user what it means.

6. **Audit log is sacred.** No delete, no edit, no archive, no hide. Read-only, CSV-exportable, retained indefinitely. Any UI element that would allow modification of audit log records must not exist.

7. **Score badges are consistent.** Green = Pursue (≥ 10), Yellow = Partner (6–9), Grey = NoBid (< 6). Same color system everywhere it appears — queue, kanban, contact history, activity log.

8. **Sequence step previews render exactly what will be sent.** No approximations. The preview must use the same `LiquidTemplateRenderer` that the worker uses. If the preview looks wrong, the email will look wrong.

9. **Auth gates everything.** No unauthenticated routes. Entra SSO must complete before any page content loads. Redirect to login on session expiry — do not show a broken page.

10. **Settings changes are logged.** Every change to scoring config, lane config, send limits, or suppression list is written to the audit log with the actor's identity.
