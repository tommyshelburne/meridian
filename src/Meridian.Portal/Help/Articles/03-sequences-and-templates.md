---
title: Sequences and templates
summary: How outreach sequences pick contacts, render emails, and advance through steps.
tags: sequences, templates, liquid, outreach
---

A **template** is the email body. A **sequence** chains templates into a multi-step outreach flow.

## Template Liquid tokens

Available everywhere a template renders:

| Token | Example |
|---|---|
| `{{contact.first_name}}` | Jordan |
| `{{contact.name}}` | Jordan Buyer |
| `{{contact.title}}` | Procurement Officer |
| `{{contact.email}}` | jordan@example.gov |
| `{{agency.name}}` | Utah Department of Technology Services |
| `{{opportunity.title}}` | Contact Center Modernization — 200 seats |
| `{{opportunity.deadline}}` | May 28, 2026 |
| `{{sequence.step}}` | 1 |

## Sequence selection

When the pipeline auto-enrolls a new opportunity, it picks the **first sequence whose AgencyType matches** the opportunity's agency. If no exact match exists, the **first sequence in the workspace** is used as a fallback.

This means a tenant with a single generic sequence works fine for v3.0; tenants who want type-specific routing should create one sequence per AgencyType.

## Snapshot on enrollment

When a contact is enrolled, the sequence's current state is **snapshotted as JSON** on the enrollment row. Later edits to the template body or sequence steps don't change in-flight enrollments — they keep sending whatever was true at enrollment time. This is intentional: prevents in-flight content from changing under the recipient's nose.

For v3.0, templates and sequences cannot be edited or deleted from the UI. Create new ones and let unused sequences age out (the list shows `LastUsedAt`).

## Send windows + jitter

Each step has a UTC send window (default 14:00–22:00 = 8 AM–4 PM Eastern) and an optional jitter in minutes. The SequenceJob runs every 2 hours; if a due step's window is open, it sends with a random delay up to the jitter to avoid burst patterns.

## Steps and delays

Step 1 fires immediately on enrollment (delay = 0). Each subsequent step waits `delayDays` after the previous step was sent. After the final step sends, the enrollment moves to `Completed`.
