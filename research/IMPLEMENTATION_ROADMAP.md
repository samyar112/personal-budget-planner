# Implementation Roadmap

Updated 2026-11-24. Two horizons:

- **V1 — target Friday 2026-11-27** (confirmed 2026-11-24). Every contracted product deliverable in #51
  that is code, including all AI integration and PDF upload. Correct numbers first, then the privacy layer,
  then AI.
- **Cut from V1 (confirmed):** merging PDF data into main financials (statement view ships; merge is
  post-V1), health-score pillars beyond savings rate and emergency fund, and a separate classification
  issue (folded into #33).
- **Later.** Debt data, forecasting, goals, net worth, investments, transfer pair-matching. New scope; each
  needs its own issue.

## V1 plan (Friday) — this supersedes the slice table and schedule below where they differ

| Day | Slices | Acceptance notes |
|---|---|---|
| Tue 24 | **#33** summary fix + category mapping folded in | Income by category; refunds reduce spending; "Net cash flow"; calendar months; one mapping table over Plaid v1 and v2 detailed codes; cash and P2P count as spending; v2 loan disbursements never income |
| Wed 25 | **#32** chart, **#37** recurring (simple detector) | as in the slice table |
| Thu 26 | **#31** PDF upload + separate statement view, **#39** trial banner | PDF accounts `IncludeInTotals = false`; no merge in V1 |
| Fri 27 | **#34** payload + validator, **#35** preview, **#36** AI tips, **#38** score (pillars 1 and 2, "early version") | Number validator stays in; if not green by Friday afternoon, ship rule-based tips |

Approvals to give on Tuesday so nothing blocks Thursday/Friday: PdfPig; AI provider and `Anthropic` NuGet vs
plain `HttpClient`; writing issue criteria to GitHub. Leave Friday time for the user's click-through of the
PDF and AI screens.

Project rules still apply to every slice: plan approved before code, one issue / one branch / one session,
tests fail before they pass, build and test output quoted.

Ordering logic: **correct numbers before more numbers.** The chart, recurring detection, score and AI all
consume the classification; building them on today's sign-based income bakes the error in.

---

## V1 slices

| # | Slice | Issue | Depends on | Acceptance criteria (proposed; issues stay the source of truth) | Size |
|---|---|---|---|---|---|
| 1 | **Summary fix** | #33 | — | Income = income categories only; a card refund lowers Expenses and leaves Income unchanged; "Savings" renamed "Net cash flow"; calendar-month figures; "posted transactions only" note; tiles still sum to totals | S |
| 2 | **Classification-lite** | new | 1 | `FlowType` column + backfill; one mapping table over Plaid v1 **and** v2 detailed codes (audit H5); cash withdrawals and P2P payments count as spending; card payments excluded; v2 loan disbursement never Income; worked example in `TRANSACTION_NORMALIZATION.md` §3 matches except rows needing pair-matching (14–15), which are documented as a known V1 limit | M |
| 3 | **Spending chart** | #32 | 2 | Monthly category totals; refunds net within category; essential/discretionary toggle | S |
| 4 | **PDF upload — separate, then merge** | #31 | 2 | Text-layer PDF parsed in memory and discarded; rows classified like Plaid rows; account created with `IncludeInTotals = false`; statement view; merge as standalone or into a linked account with duplicates marked `DuplicateOf`; un-merge restores; same statement twice adds nothing; scanned PDF → "needs manual review" (`TRANSACTION_NORMALIZATION.md` §5.1) | L |
| 5 | **Recurring bills** | #37 | 2 | Own detector (no paid Plaid endpoint); monthly stream found from 3 occurrences; one-off spend excluded; `Bills.tsx` shows real streams | M |
| 6 | **Health score v1** | #38 | 2, 5 | Pillars 1, 2, 4, 6 of `FINANCIAL_HEALTH_MODEL.md` (no liabilities); golden test for every scoring function; score moves when spending changes; missing pillars re-weighted and listed; label says it is our own score | M |
| 7 | **AI payload + PII validator** | #34 | 6 | Allowlist payload (numbers and category codes only); validator rejects masks, names, emails, 6+ digit runs; only included accounts | M |
| 8 | **Privacy preview** | #35 | 7 | Shows the exact payload that would be sent, and which accounts it covers | S |
| 9 | **AI tips** | #36 | 7, 8 | Rule candidates → LLM ranks and explains → number validator → template fallback; 3–5 tips; works offline with a fake model in tests | M |
| 10 | **Trial banner + paywall mock** | #39 | — | Contracted UI-only deliverable; can run in parallel any day | S |

Needs approval before the slice starts:
- Slice 4: **PdfPig** (`backend.csproj`) and the #31 acceptance criteria written to GitHub.
- Slice 9: the AI provider; `Anthropic` NuGet package **or** the plain `HttpClient` decided on 2026-11-16;
  the API key entered by you in git-ignored settings.
- Slice 2: a new GitHub issue for classification-lite.

## Schedule to Sunday

Ten slices in six days is roughly two merged slices a day, with **Thursday 2026-11-26 being
Thanksgiving**. That is tight; the descope triggers below are what make it survivable.

| Day | Slices |
|---|---|
| Tue 24 | 1 |
| Wed 25 | 2 |
| Thu 26 | 3, 10 |
| Fri 27 | 4 |
| Sat 28 | 5, 6 |
| Sun 29 | 7, 8, 9 |

### Descope triggers (dated)

| If, by… | then… |
|---|---|
| Wed 25 end of day, slice 2 not merged | ship V1 on slice 1's category rule alone; classification becomes post-V1; chart and score read categories directly |
| Fri 27 end of day, slice 4 not merged | ship PDF upload with the statement view only; the merge into main financials becomes post-V1 |
| Sat 28 end of day, slice 6 not merged | score ships with pillars 1 and 2 only, labelled "early version" |
| Sun 29 midday, slice 9 validator not green | ship template tips (rule candidates, no LLM) and label them; LLM turns on after |

Hosting (D1–D6), CI, docs (P15–P18) and verification (T1–T6) are **not** in this week's plan; they are
their own milestones and follow V1.

---

## Later (post-V1, new issues)

| Phase | Content | Depends on |
|---|---|---|
| L1 Transfer pair-matching | Match both sides of transfers across linked accounts; reconciliation check | V1 slice 2 |
| L2 Debt data | Plaid `liabilities` (billed subscription) + `transactions.days_requested` at the maximum in **one** re-link; due dates, minimum payments, avalanche vs snowball | L1 |
| L3 Forecasting and goals | 60-day balance forecast from recurring streams with p10/p50/p90; overdraft alert; goals with required monthly saving | V1 slice 5, balance snapshots |
| L4 Wealth | Balance snapshots → net-worth trend; Plaid Investments | L2 |
| L5 Score v2 | Debt and income-stability pillars; calibration against optional CFPB 5-item survey | L2, 6+ months of data |

## Testing strategy summary

- Golden-value tests for every formula using the worked examples in these documents.
- Invariant tests for classification (`TRANSACTION_NORMALIZATION.md` §9), plus PDF merge: merging then
  un-merging leaves totals exactly as before.
- Endpoint tests via the existing `AuthWebApplicationFactory`.
- AI: offline fake model; a fixed set of synthetic payloads run on every prompt change; any unmatched number fails.
- Later, needs approval: a Postgres-backed test run for SQL that EF InMemory cannot check.
