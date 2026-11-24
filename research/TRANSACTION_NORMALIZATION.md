# Multi-Account Transaction Normalisation

**Problem.** With several linked institutions, one economic event can appear as two ledger rows
(transfer out of checking + transfer into savings; card payment out of checking + payment received on
the card). Summing rows double-counts. Excluding by category (what the code does today,
`TransactionCategory.CountedInSummaries`) both *misses* mislabelled internal moves and *drops* real
spending that Plaid files under `TRANSFER_OUT` (cash withdrawals, P2P payments to other people).

**Principle.** Separate two questions that the ledger currently answers with one column:

| Column | Question | Example values |
|---|---|---|
| `Category` / `Subcategory` (exists) | *What* was it? | `FOOD_AND_DRINK_GROCERIES` |
| `FlowType` (new) | *How does it move household wealth?* | `Expense`, `InternalTransfer` |

Every metric in `FINANCIAL_FORMULAS.md` reads `FlowType`. Category is for breakdown charts only.

---

## 1. Flow types

| FlowType | Counts in income | Counts in spending | Changes net worth | Typical source |
|---|---|---|---|---|
| `Income` | yes | — | + | `INCOME_*` codes, matched payroll stream |
| `Expense` | — | yes | − | everything that leaves the household for goods/services |
| `Refund` | — | **reduces** spending in its category | + | inflow on a card / merchant reversal |
| `InternalTransfer` | — | — | 0 | both legs matched within the user's linked accounts |
| `DebtPayment` | — | — (separate debt-service line) | 0 (cash ↓, liability ↓) | loan payments; card payments |
| `SavingsContribution` | — | — | 0 | transfer to a savings account that is **not** linked |
| `InvestmentContribution` / `InvestmentWithdrawal` | — | — | 0 | brokerage/retirement transfers |
| `LoanProceeds` | — | — | 0 (cash ↑, liability ↑) | `TRANSFER_IN_CASH_ADVANCES_AND_LOANS` |
| `ExternalTransferIn` | **no** (shown separately) | — | + | money from another person, unexplained deposits |
| `Fee` / `Interest` | — | yes (own category) | − | `BANK_FEES_*`, card interest charges |
| `Unclassified` | — | — | ? | low confidence; shown to the user for one-tap fixing |

`ExternalTransferIn` is deliberately not income: Venmo reimbursements for a shared dinner would otherwise
inflate income. If the user marks a sender as income (side gig), a rule makes it `Income`.

---

## 2. Classification pipeline (ordered; first decisive step wins)

```
 1. Structural  — account type + sign            (deterministic)
 2. Pair match  — internal transfer / card payment across linked accounts   (deterministic)
 3. Plaid PFC   — detailed code → FlowType table  (deterministic mapping of Plaid's ML output)
 4. User rules  — "payee X is always Y"           (deterministic)
 5. Keyword rules — sign-aware                    (deterministic)
 6. Recurring-stream inference — e.g. same amount every 14 days into checking = Income (statistical)
 7. AI tier     — only for rows still Unclassified/OTHER; cleaned merchant string + sign + account type
 8. Fallback    — Unclassified
```

Steps 1–5 are deterministic and unit-tested; their output is final. Steps 6–7 write
`ClassificationSource = Inferred | Ai` with a confidence, and never override 1–5 or a user edit.
User edits always win and are remembered as rules (step 4).

**Why not ML for everything:** transfer detection and card-payment handling are *accounting identities*,
not predictions — a wrong guess silently double-counts money. Category (*what* was bought) is the
genuinely fuzzy part, and Plaid already supplies an ML prediction with a confidence level for it.

### 2.1 Structural rules (step 1)

| Account type | Sign | Default before other evidence |
|---|---|---|
| `credit` | out (purchase) | `Expense` |
| `credit` | in | `Refund`, unless matched as card payment (step 2) or Plaid says `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` / `TRANSFER_IN_*` |
| `loan` | in | `DebtPayment` (the liability side) — excluded from cash flow, used for payoff tracking |
| `investment` | any | excluded from cash flow; used only for net worth |
| `depository` | any | continue to step 2 |

### 2.2 Pair matching (step 2) — the core of double-count prevention

Candidate pair `(o, i)`: `o` outflow on account A, `i` inflow on account B, same user, `A ≠ B`,
both accounts linked, and

- `|o.Amount| = |i.Amount|` exactly (cents) — transfers between banks are not FX-converted in a US-only app;
- `|o.Date − i.Date| ≤ 5 calendar days` (ACH between institutions commonly settles in 1–3 business days) **[design]**;
- at least one side carries a transfer signal: Plaid `TRANSFER_*` or `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT`,
  or a description containing a transfer keyword, **or** B is a `credit` account (card payment).

Resolve with a greedy match on smallest date gap, then smallest `|amount|` ambiguity; a row joins at most
one pair. Store the pair as `TransferPairId` on both rows. Matched legs:

- depository → depository: both `InternalTransfer`;
- depository → credit: outflow = `DebtPayment` (subtype card), inflow on card = `InternalTransfer` leg;
  **neither** is spending (purchases on the card already were);
- depository → loan: outflow `DebtPayment`, loan-side inflow ignored for cash flow.

**Unmatched** transfer-coded rows (the other leg is at an unlinked account):

| Plaid detailed code (v1) | Unmatched FlowType |
|---|---|
| `TRANSFER_OUT_SAVINGS` | `SavingsContribution` |
| `TRANSFER_OUT_INVESTMENT_AND_RETIREMENT_FUNDS` | `InvestmentContribution` |
| `TRANSFER_IN_INVESTMENT_AND_RETIREMENT_FUNDS` | `InvestmentWithdrawal` |
| `TRANSFER_OUT_WITHDRAWAL` (ATM cash) | `Expense` (category "Cash") |
| `TRANSFER_OUT_ACCOUNT_TRANSFER` / `TRANSFER_OUT_OTHER_TRANSFER_OUT` | `Expense` if the counterparty looks like a person/P2P app; else `Unclassified` and ask once |
| `TRANSFER_IN_CASH_ADVANCES_AND_LOANS` | `LoanProceeds` |
| `TRANSFER_IN_DEPOSIT` / `TRANSFER_IN_ACCOUNT_TRANSFER` / `..._OTHER_TRANSFER_IN` | `ExternalTransferIn` |
| `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` (card not linked) | `DebtPayment` — **counts as debt service** (purchases are invisible, so this is the best proxy for that spending; flag coverage) |
| other `LOAN_PAYMENTS_*` | `DebtPayment` |

**Taxonomy v2** (all Plaid customers since December 2025; checked against
`https://plaid.com/documents/pfc-taxonomy-all.csv`). The table above uses v1 codes. Add for v2:

| v2 code | Unmatched FlowType |
|---|---|
| `TRANSFER_OUT_TRANSFER_OUT_FROM_APPS` (P2P apps) | `Expense` after one user confirmation per payee; else `Unclassified` |
| `TRANSFER_IN_TRANSFER_IN_FROM_APPS` | `ExternalTransferIn` |
| `TRANSFER_OUT_WIRE` / `TRANSFER_IN_WIRE` | same as `ACCOUNT_TRANSFER` rows |
| `TRANSFER_OUT_CRYPTO` | `InvestmentContribution` |
| `LOAN_DISBURSEMENTS_*` (replaces v1 `TRANSFER_IN_CASH_ADVANCES_AND_LOANS`) | `LoanProceeds` |
| `LOAN_PAYMENTS_BNPL`, `_CASH_ADVANCES`, `_EWA` | `DebtPayment` |
| `INCOME_SALARY` (v1 `INCOME_WAGES`), `INCOME_CONTRACTOR`, `INCOME_GIG_ECONOMY`, `INCOME_RENTAL`, … | `Income` |
| `OTHER_OTHER` | continue to step 4 |

`TRANSFER_*_ACCOUNT_TRANSFER` and `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` are identical in both versions.
Map by **detailed code through one table** covering both versions, so the code works whichever version
an item receives.

**Re-classification on link.** When a new account is linked, previously unmatched rows in the last
90 days are re-run through step 2, because their partner may now exist.

---

## 3. Worked examples (one month, three linked accounts: Chase checking, Ally savings, Capital One card)

| # | Account | Description | Amount | Plaid code | FlowType | Today's code counts it as |
|---|---|---|---|---|---|---|
| 1 | Checking | ACME PAYROLL | +1,850 | `INCOME_WAGES` | Income | income ✔ |
| 2 | Checking | ACME PAYROLL | +1,850 | `INCOME_WAGES` | Income | income ✔ |
| 3 | Checking | ONLINE TRANSFER TO ALLY | −500 | `TRANSFER_OUT_SAVINGS` | InternalTransfer (pair 4) | excluded ✔ |
| 4 | Savings | TRANSFER FROM CHASE | +500 | `TRANSFER_IN_ACCOUNT_TRANSFER` | InternalTransfer (pair 3) | excluded ✔ |
| 5 | Card | WHOLE FOODS | −120 | `FOOD_AND_DRINK_GROCERIES` | Expense | expense ✔ |
| 6 | Card | ZARA | −200 | `GENERAL_MERCHANDISE_CLOTHING…` | Expense | expense ✔ |
| 7 | Card | ZARA RETURN | +200 | `GENERAL_MERCHANDISE_…` | **Refund** | **income ✘** |
| 8 | Checking | CAPITAL ONE AUTOPAY | −900 | `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` | DebtPayment (card, pair 9) | excluded ✔ |
| 9 | Card | PAYMENT THANK YOU | +900 | `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` | InternalTransfer (pair 8) | excluded ✔ |
| 10 | Checking | ZELLE TO J SMITH RENT | −1,100 | `TRANSFER_OUT_ACCOUNT_TRANSFER` | **Expense** (rent, after one user confirmation) | **excluded ✘** |
| 11 | Checking | ATM WITHDRAWAL | −100 | `TRANSFER_OUT_WITHDRAWAL` | **Expense** (cash) | **excluded ✘** |
| 12 | Checking | VENMO FROM A LEE | +45 | `TRANSFER_IN_ACCOUNT_TRANSFER` | ExternalTransferIn | excluded (✔ by luck) |
| 13 | Savings | INTEREST PAID | +4 | `INCOME_INTEREST_EARNED` | Income | income ✔ |
| 14 | Checking | TRANSFER FROM SAVINGS | +300 | **`INCOME_OTHER_INCOME`** (mis-tag) | InternalTransfer (pair 15) | **income ✘** |
| 15 | Savings | TRANSFER TO CHECKING | −300 | `TRANSFER_OUT_ACCOUNT_TRANSFER` | InternalTransfer (pair 14) | excluded |

**Expected results**

| Figure | Correct | Current code (branch `feat/33`) | Error |
|---|---|---|---|
| Income | 1,850+1,850+4 = **3,704** | 3,704 + 200 (refund) + 300 (mis-tag) = **4,204** | +13.5 % |
| Spending | 120 + 200 − 200 + 1,100 + 100 = **1,320** | 120 + 200 = **320** | −76 % |
| Net cash flow | **2,384** | **3,884** | +63 % |
| Savings rate (cash-flow) | 2,384/3,704 = **64 %** | "Savings" 3,884 / 4,204 = **92 %** | |
| Behavioural saving | 500 to savings − 300 back = **200** | not shown | |

(The example is extreme on purpose; each row is an ordinary event.)

---

## 4. Refunds and reimbursements

- A **refund** is an inflow whose category is a spending category, or any inflow on a credit account
  that is not a matched payment. It reduces spending in that category *in the month it posts*
  (simple, reconciles with statements). Optional link to the original purchase (same merchant,
  amount ≤ original, within 90 days) for display only. **[design]**
- Monthly category spend is floored at 0 for charts; the remainder carries into totals so totals still
  reconcile with the ledger.
- **Reimbursements** from people (`ExternalTransferIn`) are *not* netted automatically; the user can mark a
  sender as "splits costs with me", after which inflows from them net against spending.

## 5. Duplicates

| Source of duplicate | Detection | Action |
|---|---|---|
| Sync replay | unique `(UserId, Source, ExternalId)` — **already handled** | — |
| Pending → posted | Plaid removes the pending id and adds a posted id (posted row has `pending_transaction_id`) — **already handled** by skipping pending | keep |
| Same login linked twice | on exchange: same `institution_id` **and** overlapping account `mask`+`subtype` on an existing item | block, or offer to replace (Plaid update mode) |
| Same account via Plaid + PDF upload | only after the user maps the statement to a linked account (§5.1); then same date ±1 + same amount + similar description (token Jaccard ≥ 0.5) | keep the Plaid row, mark the PDF row `DuplicateOf`; never silently delete |
| Same statement uploaded twice | `ExternalId` = hash of (account, date, amount, description, running-line index) → existing unique index | skip on insert |
| Legitimate identical purchases (two coffees) | same as above but **both from the same source with different ExternalIds** | **not** duplicates — never dedupe within one source |

### 5.1 PDF statements: separate by default, merged by choice

Decided 2026-11-24. PDF data is a **separate view until the user merges it**. One analytics engine
serves both; separation is a per-account flag, not a second code path.

1. **Upload** creates or extends an account with `Source = PdfUpload` and `IncludeInTotals = false`.
   Rows go through the same classification pipeline as Plaid rows (keyword tier, since statements carry
   no Plaid category), so the statement view shows real income/spending/categories immediately.
2. **Statement view** = the normal summary, chart and categories filtered to that account.
3. **Merge prompt** on the statement view: *"Add this account to your main financials?"*
   - **"It's an account Plaid doesn't see"** → `IncludeInTotals = true`.
   - **"It's one of my linked accounts"** → user picks the account (`MergedIntoAccountId`). Rows that match
     a Plaid row (rule above) are marked `DuplicateOf` and excluded; unmatched rows — typically months before
     Plaid's history starts — are re-pointed to count with that account. Then `IncludeInTotals = true`.
4. **Un-merge** sets the flag back and clears `DuplicateOf` marks for that statement. Nothing is deleted;
   totals recompute.
5. **Consumers** — totals, health score, recurring detection, the AI payload — read only rows where the
   account has `IncludeInTotals = true` and `DuplicateOf IS NULL`. The privacy preview lists which
   accounts were included.

Why opt-in: a parse error stays in its own view instead of corrupting totals and the score; mapping
prevents counting a Plaid-synced account twice (audit M4, different source); the user sees keyword-only
categories before mixing them in. Older statement months fill Plaid's short history, which helps recurring
detection and the score.

Edge cases: a statement whose date range overlaps Plaid's only partly (match only inside the overlap);
statement for a **credit card** (sign convention: purchases are money out, payments money in — normalise at
parse time to the ledger convention); a scanned PDF with no text layer → reject with "needs manual review"
(OCR deferred, per the 2026-11-16 decision).

## 6. Pending versus posted

Keep the current rule for history (posted only). For the forecast, read pending rows into memory
from the latest sync and subtract them from the starting balance if `available` balance is not
used. Use `authorized_date` (when present) for month attribution of spending; posted `date` for
cash timing. **[design]**

## 7. Income

- **Recurring income**: an inflow stream (same normalised payer, cadence 7/14/15/30 days ± 3,
  amount CV < 0.2) on a depository account. Plaid `/transactions/recurring/get` returns `inflow_streams`;
  prefer it when available.
- **Irregular income** (gig, commission): `INCOME_*` rows outside any stream. Forecast with a
  conservative quantile (§6 of formulas) and flag volatility.
- **Tax refunds, unemployment, one-offs** (`INCOME_TAX_REFUND`, `INCOME_UNEMPLOYMENT`): income in history,
  **excluded** from expected income in forecasts.

## 8. Loans, cash, shared accounts, missing categories

- **Loan payments**: `DebtPayment`; principal/interest split only with Liabilities data. Mortgage payment
  goes to essential housing cost in the essential/discretionary split.
- **Cash withdrawals**: `Expense` in category Cash. Over-states spending only if cash is redeposited;
  a matching cash deposit (`TRANSFER_IN_DEPOSIT`, ≤ 30 days, ≤ amount) reduces it. **[design]**
- **Shared/joint accounts**: Plaid cannot say who owns which share. Let the user set an ownership
  share `s ∈ (0,1]` per account; household view uses 100 %, personal view multiplies flows and balances by
  `s`. Two users linking the same joint account each see their share. **[design]**
- **Missing or wrong categories**: `OTHER` and Plaid `confidence_level` `LOW`/`UNKNOWN` go to the
  AI tier (P6 note in the handoff: rule-based first, AI only for `OTHER`, cleaned merchant only), then
  to a review queue. Category errors affect breakdowns; **flow-type errors affect totals**, so the review
  queue sorts by amount × flow-type uncertainty.

## 9. Invariants to test (each a unit or integration test)

1. Σ of a matched pair's amounts = 0.
2. Linking a second account never *increases* Income for past months unless a new `Income` row arrives.
3. Card purchase + card payment counted once: spending = purchase.
4. A refund never increases Income.
5. Re-running classification is idempotent (same input → same FlowType, same pair ids).
6. User override survives a re-sync of the same `ExternalId`.
7. Totals = Σ per-account figures (existing invariant, keep it).
8. **Reconciliation check.** For a complete month, the sum of every ledger row on linked depository and
   credit accounts must equal Δ(depository balances) − Δ(card balances owed), within the pending
   tolerance. Separately, `Income − Spending − DebtService − SavingsContribution − InvestmentContribution
   + ExternalTransferIn + LoanProceeds + InvestmentWithdrawal` must equal that same sum, since internal
   pairs net to zero. The first catches missing rows; the second catches classification bugs. Report both
   residuals per month (needs balance snapshots).
