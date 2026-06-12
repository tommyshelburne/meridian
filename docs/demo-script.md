# Meridian Prospect Demo Script (20–30 min)

A guided walkthrough of the live product at https://meridianbd.dev using the
isolated demo tenant. Everything below assumes the tenant was provisioned (or
reset) with the seeded demo story — see "Before the call".

The demo tenant is real production: real scoring engine, real sequence
engine, real multi-tenant isolation. The only thing faked is delivery — the
tenant's outbound provider is `Console`, so sequences run end-to-end and
record full history while no email ever leaves the box. All seeded contact
addresses additionally use RFC 2606 reserved domains (`example.com/org/net`).

## Before the call (5 min, day-of)

```bash
scripts/demo.sh reset demo-meridian          # pristine story, idempotent
```

Then verify:

1. Log in at `/login` as the demo operator. First screen should be the
   dashboard at `/app/demo-meridian`.
2. Pipeline board (`/app/demo-meridian/pipeline`) shows cards in multiple
   columns.
3. Replies (`/app/demo-meridian/replies`) shows Dan Whitfield's reply.
4. Keep a terminal ready on burrow for the live-processing beat (Act 4):

```bash
sudo systemd-run --pipe --wait --collect --uid=meridian --gid=meridian \
  -p EnvironmentFile=/etc/meridian/worker.env \
  --working-directory=/opt/meridian/worker \
  /usr/bin/env dotnet Meridian.Worker.dll --run-job Processing
# then, for the enrollment/send beat:
#   ... --run-job Sequence
```

If a previous demo left the tenant dirty and you only notice mid-call, keep
going — mutations are part of the product story. Reset after, not during.

## The story arc

Frame for a BD decision-maker at a government contractor: *"Your team's
problem isn't finding opportunities — it's that everything looks like an
opportunity. Meridian finds the ones worth your pipeline and starts the
conversation for you."*

### Act 1 — The morning queue (5 min)

Route: `/app/demo-meridian/opportunities`

- The queue is what their analyst would otherwise build by hand from SAM.gov
  tabs: each row already scored, seat-estimated, deadline-tracked.
- Open **VA Helpdesk Contact Center Modernization** (MDEMO-001). Walk the
  score breakdown rows — lane fit, win themes, vehicle, value. This is the
  "why should I care" answer on one screen.
- Contrast: **Janitorial Services — Federal Building Annex** sits Rejected.
  Meridian read the same feed their inbox does and binned the noise.

### Act 2 — Deciding, not re-reading (5 min)

Route: `/app/demo-meridian/pipeline`

- The kanban is decisions, not documents: Pending Review, Pursuing,
  Partnering, Watching. Each seeded card is a different call your team
  already understands (pursue the VA recompete, partner on the SSA prime
  teaming, watch DLA until CMMC scope is clear).
- Re-decide one card live (move Texas DIR from Pending Review to Pursuing) —
  show the board update. This is the daily standup artifact.

### Act 3 — Outreach that starts itself (7 min)

Routes: `/app/demo-meridian/settings/sequences`, then `/replies`

- Sequences: two playbooks (Federal RFP, State & Local), each a few steps
  with delays, business-hours send windows, jitter. Templates merge
  opportunity, agency, and contact fields.
- The compliance angle (their lawyers will ask): physical address +
  unsubscribe link are enforced per tenant, opt-outs and bounces suppress
  automatically, soft bounces escalate after three.
- Replies view: **Dan Whitfield (Colorado OIT) replied** asking for a
  Thursday call. That email was step 1 of a sequence nobody on the team
  wrote, sent while nobody was watching. This is the money moment — pause
  on it.

### Act 4 — Live: a new opportunity lands (5 min)

Routes: `/app/demo-meridian/opportunities`, terminal alongside

- The **City of Phoenix 311 replacement** is sitting unscored — it "arrived
  this morning."
- Run `--run-job Processing` in the terminal. Refresh: scored, seat
  estimate, queued for review. Then `--run-job Sequence`: the enrollment
  fires (sandboxed) and the activity trail appears.
- Point: nothing you watched was staged for the demo — that is the
  production pipeline doing its scheduled job, on demand.

### Act 5 — Close (3–5 min)

Routes: `/app/demo-meridian` (dashboard), `/app/demo-meridian/activity`,
`/pricing`

- Dashboard recaps the funnel the demo just walked: ingested → scored →
  decided → contacted → replied.
- Activity feed = the audit trail (gov-adjacent buyers care).
- Mention, don't tour: source wizard (bring your own feeds), CRM sync
  (HubSpot/Pipedrive/Salesforce), SSO, members/roles.
- End on pricing and the pilot ask.

## Q&A you should expect

- **"Is this sending real email?"** In this demo tenant, no — the outbound
  provider is a sandbox; you saw the full send path and history without
  delivery. A real tenant plugs in their provider and domain on the
  Outbound settings page.
- **"Whose data am I looking at?"** Seeded fixtures in an isolated tenant.
  Tenant isolation is enforced at the query layer for every table — your
  workspace can't see this one, and vice versa.
- **"What does ingestion actually watch?"** SAM.gov, USASpending, bid-match
  emails, RSS/REST/webhook sources via the wizard — show
  `/app/demo-meridian/sources` if asked.

## After the call

```bash
scripts/demo.sh reset demo-meridian
```

Reset wipes and re-seeds all demo data; the operator login survives. To
stand up a fresh tenant for a named prospect instead (e.g. a leave-behind),
provision a second one — slugs must start with `demo-`:

```bash
scripts/demo.sh provision demo-acme demo-acme@meridianbd.dev "Acme Demo"
```

The operator email needs no real mailbox: the login is pre-verified and the
tenant cannot send mail.
