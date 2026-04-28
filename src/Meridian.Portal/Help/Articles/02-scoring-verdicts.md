---
title: Scoring verdicts — Pursue, Partner, NoBid
summary: How the rule-based scoring engine decides what to do with a new opportunity.
tags: scoring, opportunities, verdicts
---

Every newly ingested opportunity goes through the rule-based scoring engine and gets one of three verdicts.

## How the score is computed

Eight signals combine into a total (max 14). See **Settings → Scoring** for the rule weights.

| Signal | Max points |
|---|---|
| Title matches lane keywords | 2 |
| Description matches lane keywords | 1 |
| Agency tier (federal / Tier-1 state / etc.) | 2 |
| Win-theme keywords + legacy-incumbent flags | 2 |
| Past-performance NAICS match | 2 |
| Procurement vehicle (GSA eBuy, NASPO, Sourcewell, etc.) | 2 |
| Estimated seat count | 2 |
| Recompete detected | 1 |

## Verdicts

- **Pursue** — total ≥ Pursue threshold. Status moves to `Scored`. POC enrichment + CRM deal + auto-enrollment all fire.
- **Partner** — between Partner and Pursue thresholds. Same downstream actions as Pursue, but flagged for teaming partners rather than direct bid.
- **NoBid** — below the Partner threshold. Status moves to `NoBid` and downstream stages are skipped. Opportunity is preserved in the database for reporting but no contact is created and no email goes out.

## Tuning

Change thresholds and keyword lists at **Settings → Scoring**. Changes apply to opportunities scored *after* the change — already-scored opps keep their original verdict. To re-score an opportunity, delete it and let the next ingestion run pull it back in (or use the dev seed endpoint in non-production environments).
