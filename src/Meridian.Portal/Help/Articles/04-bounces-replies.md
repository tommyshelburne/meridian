---
title: Bounces, replies, and unsubscribes
summary: How Meridian handles deliverability signals from Resend and the broader email ecosystem.
tags: bounces, replies, unsubscribes, deliverability, resend, can-spam
---

Outbound email is only useful if the bounce/reply loop closes back into Meridian. Configure the Resend webhook and the loop is automatic.

## Setup

1. **Settings → Outbound** — paste the Resend signing secret into "Resend webhook signing secret".
2. In your Resend dashboard, add a webhook pointing at:

   ```
   https://<your-portal-domain>/api/webhooks/resend/{tenantId}
   ```

   Replace `{tenantId}` with the tenant Guid (visible in the URL of any Portal page or via SQL).

3. Enable the events: `email.bounced`, `email.complained`, `email.delivery_delayed`.

## What happens

| Resend event | Meridian behavior |
|---|---|
| `email.bounced` (hard) | Contact marked as `IsBounced`. All active enrollments for that contact move to `Bounced`. Contact added to suppression list — no future sends. |
| `email.bounced` (soft) | `SoftBounceCount` incremented. After 3 soft bounces the contact is escalated to permanently bounced (same effect as hard). |
| `email.complained` | Contact marked `IsOptedOut`, added to suppression as both email and (optionally) domain. |
| Reply received (via Graph mailbox poll, every 4h) | Active enrollment moves to `Replied`. Email activity stamped with reply timestamp. |

## Manual unsubscribes

The compliance footer appended to every email includes an unsubscribe link pointing at the URL you configured at **Settings → Outbound**. Clicking it should hit your unsubscribe endpoint and add the recipient to suppression — that endpoint is operator-built; Meridian doesn't host it.

## Suppression list

Three sources contribute:
- Hard bounces
- Spam complaints
- Manual unsubscribe (via your endpoint)

Suppressed addresses are checked **before every send** by `SuppressionFilterEmailSender`. A suppressed send returns failure and the enrollment moves to `Unsubscribed` automatically.
