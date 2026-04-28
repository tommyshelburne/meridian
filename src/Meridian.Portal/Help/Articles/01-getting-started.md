---
title: Getting started
summary: First-day checklist for a new tenant.
tags: setup, onboarding
---

If you just signed up, here's the order to set things up so the pipeline can fire end-to-end.

## 1. Add a source

Open **Sources** in the sidebar and add at least one. SAM.gov is the safest start for federal opportunities; state portals (Utah's MyBidMatch, generic RSS/REST/HTML) cover the rest. Sources can be enabled/disabled without deleting them.

## 2. Configure outbound email

**Settings → Outbound.** Provider = `Resend` for production, `Console` to dry-run without actually sending. You'll need:

- A Resend API key
- Verified sender address + display name
- A physical mailing address (CAN-SPAM)
- Unsubscribe URL
- Optional but recommended: a daily send cap (5–10 for the first day)

The webhook signing secret is optional but **strongly recommended** for bounce + complaint handling. The webhook lives at `/api/webhooks/resend/{tenantId}`.

## 3. Create at least one sequence

**Settings → Sequences.** Create one or more templates first (the email body, with Liquid tokens like `{{contact.first_name}}`), then build a sequence whose AgencyType matches the agencies your sources ingest. Up to 5 steps per sequence.

## 4. Optional — connect a CRM

**Settings → CRM.** Pipedrive, HubSpot, and Salesforce are supported. New opportunities scored as Pursue/Partner get a deal created automatically.

## 5. Wait for the worker

Once everything's configured, the worker will:

1. **Ingest** new opportunities (daily 06:00 UTC)
2. **Process** them every 30 min (score → enrich → enroll)
3. **Send** every 2h via SequenceJob

First sends typically appear within 2–3 hours of the first ingestion run.
