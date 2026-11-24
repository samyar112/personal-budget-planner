# Project Audit — VaultBudget AI

Audited 2026-11-24 on branch `feat/33-summary-cards` (two commits ahead of `main` at `383e526`).
Every claim below comes from reading the source named next to it. Severity: **High** = produces
wrong numbers a user would act on; **Medium** = limits what can be built or will cause wrong numbers
later; **Low** = polish.

Verified state: `dotnet test VaultBudget.slnx` → `Passed! - Failed: 0, Passed: 221, Skipped: 0`.

---

## 1. Architecture as built

| Layer | What exists | Source |
|---|---|---|
| API | ASP.NET Core controllers, .NET 10. `AuthController`, `AdminController`, `PlaidController`, `TransactionsController` | `backend/Controllers/` |
| Data | EF Core + Npgsql, one `AppDbContext`, no repository layer | `backend/Data/AppDbContext.cs` |
| Config | Options pattern with `ValidateOnStart` | `backend/Program.cs`, `*Options.cs` |
| Auth | JWT in HttpOnly cookies, refresh-token families, email verification, emailed login code, Google sign-in, lockout, rate limits, admin role | `backend/Services/*`, 16 migrations |
| Bank data | Plaid link → exchange → `/transactions/sync` with a cursor; AES-GCM-encrypted access token bound to the user id | `PlaidClient.cs`, `PlaidSyncService.cs`, `AccessTokenProtector.cs` |
| Categorisation | Plaid `personal_finance_category` (v1) primary + detailed, then 13 keyword rules, then `OTHER` | `TransactionCategorizer.cs`, `TransactionCategory.cs` |
| Summary | `GET api/transactions/summary?from&to` → income / expenses / "savings" per account and in total | `TransactionsController.cs:508` (branch only) |
| Frontend | React 19 + Vite + Bootstrap; pages Home, Accounts, Transactions, Spending, Bills, Insights, Admin | `frontend/src/pages/` |
| AI | **None.** No LLM client, no prompt, no provider configured | grep of `backend/` |
| Background work | One in-process queue for account emails only. Syncs are user-triggered HTTP calls | `Services/Email/AccountEmailWorker.cs` |

### Data model (financial tables only)

```
User 1─* PlaidItem 1─* Account 1─* Transaction
           (encrypted token,     (Type, Subtype,       (Date, Amount signed: − = out,
            SyncCursor,           CurrentBalance,       Category, Subcategory,
            LastSyncedAt)         BalanceAsOf, Mask)    Source, ExternalId)
```

Good choices worth keeping:
- `decimal(18,2)` for money, never `double`.
- One signed sign convention (`−` = money out) converted once at the Plaid boundary (`PlaidSyncService.cs:463`).
- Sync is atomic: all pages fetched, then rows **and** cursor saved in one `SaveChanges`; `LastSyncedAt`
  as a concurrency token stops two syncs from both advancing the cursor. Replays are idempotent via the
  unique `(UserId, Source, ExternalId)` index. This is better than most hobby Plaid integrations.
- The inclusion rule lives in one expression (`TransactionCategory.CountedInSummaries`) that runs in SQL.

---

## 2. Implemented / partial / missing

| Capability | Status | Notes |
|---|---|---|
| Multi-institution linking | **Implemented** | Unlink revokes at Plaid first |
| Transaction sync (posted) | **Implemented** | Pending rows deliberately skipped |
| Categorisation | **Implemented** | Plaid taxonomy v1; no user override |
| Account balances | **Partial** | `current` only, latest value only, no history, no `available`, no `limit` |
| Income / expense / net summary | **Partial** (branch) | Rolling 30 days; see defects H1–H4 |
| Spending breakdown chart (P7, #32) | **Missing** | `Spending.tsx` is a placeholder |
| Recurring bills / subscriptions (P8, #37) | **Missing** | `Bills.tsx` is hard-coded Netflix/Apple/Adobe |
| AI recommendations (P11, #36) | **Missing** | `Insights.tsx` is four static tips |
| PII anonymisation (P9, #34) / preview (P10, #35) | **Missing** | Decision recorded: allowlist payload, .NET only |
| Health score (P12, #38) | **Missing** | Landing page advertises "A 0–100 score based on savings, spending, and debt habits" |
| PDF statements (P6, #31) | **Missing** | Hero shows an "Upload Statement" button that does nothing |
| Debt data (APR, minimum payment, due date) | **Missing** | `liabilities` product not requested |
| Investments | **Missing** | `investments` product not requested |
| Net worth, goals, forecasts, snapshots | **Missing** | No entities, no issues |
| Internal-transfer matching across accounts | **Missing** | Relies on Plaid's category alone |

The issue list (#31–#39) covers the original contract. Net worth, goals, forecasting and debt planning
are **not tracked in any issue**, open or closed — they are new scope, not gaps in agreed scope.

---

## 3. Defects that produce wrong or misleading numbers

### H1 — Every positive amount is "income" (High)
`TransactionsController.cs:546`: `Income = Sum(Amount > 0 ? Amount : 0)` over all non-transfer rows.
So these count as income: card refunds, merchant reversals, fee reversals, cash-back statement credits,
reimbursements from friends that Plaid tags as anything other than `TRANSFER_IN`, and interest.

Example (one month): salary +3,000; a returned $200 jacket refunded to the credit card.
Today: Income 3,200, Expenses = gross spend. Correct: Income 3,000, Shopping expense reduced by 200.
Savings-rate denominators — and therefore a future health score — are inflated.

**Fix direction:** income is defined by classification (`INCOME_*` codes, or matched payroll
streams), not by sign. Refunds are negative expenses in the category they reverse
(see `TRANSACTION_NORMALIZATION.md` §4).

### H2 — "Savings" is net cash flow, and it hides real saving (High)
`Savings = Income − Expenses` (`TransactionDtos.cs`). Because transfers are excluded, a $500
transfer from checking to savings is invisible to this number; the card is labelled "Savings" while it
actually measures *unspent cash flow*. A user who saves $500 and overspends $100 sees "Savings −$100".
**Fix direction:** rename to "Net cash flow"; report *saving* separately as net transfers into
savings/investment accounts (§3 of `FINANCIAL_FORMULAS.md`).

### H3 — Category-based transfer exclusion drops real spending and can double-count (High)
`CountedInSummaries` removes every `TRANSFER_IN` / `TRANSFER_OUT` row. In Plaid's taxonomy these
primaries also cover ATM cash withdrawals, payments to other people via P2P apps, and
loan/cash-advance proceeds. A user paying rent by Zelle has **no rent expense**. Conversely, a
genuine move between two of the user's own accounts that Plaid categorises as something else
(common across institutions) is counted twice: once as expense, once as income.
**Fix direction:** decide "internal" by *matching both legs* across the user's linked accounts, and treat
unmatched transfers to third parties as expenses (`TRANSACTION_NORMALIZATION.md` §3).

### H4 — Keyword fallback ignores direction and order (Medium)
`TransactionCategorizer.cs:197`: the first rule (`TRANSFER|ZELLE|VENMO|PAYMENT THANK YOU`) maps to
`TRANSFER_OUT` whether money went in or out; a description such as "ACH TRANSFER PAYROLL ACME"
matches `TRANSFER` before `PAYROLL`, so a salary vanishes from income. `BP` as a whole word will
also match unrelated descriptors. These are fallback-only today (Plaid rows usually carry a
category), but P6 PDF rows will depend on them entirely.

### H5 — Plaid category taxonomy v2 is not handled (High if this Plaid team is on v2)
`TransactionCategory.cs` hard-codes Plaid's **v1** taxonomy (16 primaries) and the sync request sends
no `personal_finance_category_version`. Plaid's taxonomy CSV states: "All new customers after
December 2025 are on PFCv2." v2 adds the primaries `LOAN_DISBURSEMENTS` and `OTHER`, renames
`INCOME_WAGES` → `INCOME_SALARY`, and replaces `TRANSFER_IN_CASH_ADVANCES_AND_LOANS` with
`LOAN_DISBURSEMENTS_*`. Under v2 a personal-loan or cash-advance deposit is not a recognised primary,
falls through to keyword rules, and as a positive non-transfer row **is counted as income** by the
summary. Which version this team receives is unverified; check the `version` field Plaid returns
(not currently parsed) on one sandbox sync before relying on either.

### M1 — Credit-card refunds are split inconsistently (Medium)
The per-account card for a credit account shows "Spent" (`AccountSummaryCards.tsx`) and hides its
income, yet the refund on that card is still in the headline Income total. The tiles do not add up
the way the UI says they do.

### M2 — Rolling 30-day window (Medium)
`Home.tsx` asks for "last 29 days + today". Bi-weekly pay lands 2 or 3 times in a 30-day window,
so income swings ±50 % with no change in the user's situation. Trends, ratios and the health score
need calendar months (or normalised monthly equivalents).

### M3 — Only ~90 days of history, no debt or investment data (Medium)
`PlaidClient.cs:95` requests `products: ["transactions"]` with no `transactions.days_requested`, so
Plaid's default history applies (the default length was not verified in this research; check the
link-token docs). Plaid recommends at least **180 days** for `/transactions/recurring/get`, and
seasonal models need 24+ months, so request the maximum. No `liabilities` → no APR, minimum
payment or due date; no `investments` → no holdings. Requesting more later needs every user to
re-link (Plaid update mode).

### M4 — Same bank linked twice is not prevented (Medium)
`PlaidItem.InstitutionId` is stored "so a later slice can spot the same bank linked twice", but
nothing checks it. A second link of the same login yields new `account_id`s and new
`transaction_id`s, so the unique index does not catch it and **every transaction is counted twice**.

### M5 — Balance is a single overwritten value (Medium)
`Account.CurrentBalance` is replaced on each sync. Net-worth trends, emergency-fund trajectory and
forecast back-testing all need dated balance snapshots. `available` balance and credit `limit`
(needed for utilisation) are discarded at `PlaidClient.cs:146`.

### M6 — Pending transactions are invisible (Low–Medium, deliberate)
Skipping pending rows avoids double display, but the most recent 1–5 days of card spending are
missing from "last 30 days", and a cash-flow forecast starting "today" will be optimistic. Document
it in the UI ("posted transactions only") or store pending rows flagged and exclude them from
historical aggregates while including them in the near-term forecast.

### L1 — Smaller items
- Posted `date` is used, not `authorized_date`; purchases near month-end land in the next month.
- `iso_currency_code` is ignored and `formatMoney` assumes USD. Fine while `country_codes` is US-only; say so.
- No user override of a category; once AI tiers exist this becomes necessary for trust.
- Static Insights tip "Build a 6-month emergency fund" is shown as if personalised.

---

## 4. Architectural risks

1. **No classification layer.** Category (what was bought) and *flow type* (income / spend / transfer /
   debt / saving) are conflated in one column. Every consumer re-derives flow type from category.
   This is the root of H1–H3 and the single most important change before any further metric.
2. **No time dimension for derived data.** Nothing records a metric as of a date, so a score
   cannot show change, and an AI tip cannot be reproduced later.
3. **User-triggered sync only.** Fine for the demo; a scheduled sync (or Plaid
   `SYNC_UPDATES_AVAILABLE` webhook) is needed before snapshots or alerts mean anything.
4. **Pi hosting.** Any heavy model (ARIMA fits per user, ML classifiers) competes with Postgres for a
   few GB of RAM. Favour closed-form calculations (see `FORECASTING` in `FINANCIAL_FORMULAS.md` §6).
5. **Advice liability.** The product copy promises recommendations and a score. Without a documented
   method and visible assumptions, both are "financially misleading" in exactly the sense the brief asks
   us to flag. `FINANCIAL_HEALTH_MODEL.md` and `AI_FINANCIAL_ADVISOR.md` address this.

## 5. Technical debt not related to finance (from `.claude/handoff.md`, re-checked)
- `CLAUDE.md` test count (says 18) is stale: 221 pass on this branch.
- Two known lint errors (`BrandName.tsx:8`, `Landing.tsx:30`).
- Production config, Data Protection key persistence and Pi deploy slices D1–D6 are open.
