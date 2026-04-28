---
title: Troubleshooting — "no emails are going out"
summary: The most common reasons the pipeline silently no-ops, and how to diagnose each.
tags: troubleshooting, debugging, sequences, processing-job, diagnostics
---

If you set everything up and the worker isn't sending, walk this checklist top to bottom. Each stage of the pipeline can silently no-op if a prerequisite is missing.

## 1. Are opportunities being ingested?

**Settings → Sources** shows last-run timestamps. If a source hasn't run since you enabled it, the worker isn't reaching it (or hasn't ticked yet — IngestionJob runs daily at 06:00 UTC).

Check the Portal **Opportunities** list. If empty, ingestion isn't producing rows.

## 2. Are opportunities being scored?

Status `New` means ProcessingJob hasn't picked it up yet (every 30 min). If opportunities have been sitting at `New` for more than an hour, either the worker isn't running or the tenant has no active sources.

After processing: status `Scored` (verdict Pursue/Partner) or `NoBid`. Anything still at `New` after a few hours = ProcessingJob is broken.

## 3. Did POC enrichment find a contact?

Open an opportunity detail page. If "Contacts" is empty for a Pursue/Partner opp, no enricher returned a contact. In production this typically means the SAM.gov POC API didn't have a contact for that notice, or the USASpending lookup didn't match.

**Workaround:** add the contact manually via the **Enrichment** queue page.

## 4. Is there a sequence configured?

**Settings → Sequences.** If empty, ProcessingJob auto-enrollment skips every opportunity. The pipeline picks the first sequence whose AgencyType matches the opportunity, falling back to any tenant sequence — but it needs at least one to exist.

## 5. Is the contact enrollable?

The contact must:
- Have a non-null email
- Not be opted out
- Not be marked bounced
- Have a confidence score ≥ 0.5

Manually-added contacts default to confidence 1.0; auto-enriched contacts vary by source. Low-confidence contacts won't enroll.

## 6. Is the daily cap hit?

**Settings → Outbound → Daily send cap.** SequenceJob stops sending for the day once the cap is reached and logs `Daily send cap reached for tenant`. Reset at 00:00 UTC daily.

## 7. Is OutboundConfiguration set up + enabled?

Without it, `TenantRoutedEmailSender` falls back to no-op. Verify **Settings → Outbound** shows "Enabled: True" and a provider other than... actually, even Console works for local dry runs. The blocker is **missing** OutboundConfiguration entirely.

## 8. Are sends in the send window?

Each step has a UTC send window. Default is 14:00–22:00 UTC (8 AM–4 PM Eastern). Outside the window, the SequenceJob sees the enrollment as due but skips it until the window opens.

## Where to look in the data

The most authoritative trace is the audit log: **Activity** in the sidebar. Every stage emits an audit event:

- `OpportunityScored`
- `DealCreated`
- `EnrollmentCreated`
- `EmailSent`

If you see Scored but not EnrollmentCreated, the gap is at step 4 or 5 above. If you see EnrollmentCreated but not EmailSent, look at steps 6–8.
