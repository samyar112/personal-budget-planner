# Technical Architecture

Fits the existing codebase: controllers, `AppDbContext` used directly, Options with `ValidateOnStart`,
positional `sealed record` DTOs, static pure services where there is no I/O (as
`TransactionCategorizer`). **No** MediatR, repositories, AutoMapper, FluentValidation or `Result<T>`.
No new NuGet packages are required for anything below except the optional AI SDK.

## 1. Component view

```
                ┌──────────── API (ASP.NET Core controllers) ─────────────┐
 Plaid ──sync──►│ PlaidSyncService ─► LedgerWriter ─► ClassificationService│
 PDF  ──upload─►│ (P6 parser)                       │  (TRANSACTION_NORM.) │
                │                                   ▼                      │
                │                     Transactions (+FlowType, PairId)     │
                │                                   │                      │
                │   MonthlySummaryBuilder ◄─────────┤                      │
                │          │                        │                      │
                │          ▼                        ▼                      │
                │   MetricsService (pure calculators) ─► HealthScoreService│
                │          │                                   │           │
                │          ▼                                   ▼           │
                │   ForecastService                     AdvisorService ────┼──► LLM
                │                                                          │
                │   BackgroundService: nightly sync + snapshot + recompute │
                └──────────────────────────────────────────────────────────┘
```

## 2. Database changes

Principle: **store facts, derive views.** Transactions, balances and user inputs are facts;
monthly summaries and scores are cached derivations that can be rebuilt from facts at any time,
keyed by a `CalcVersion` so a formula change triggers a rebuild rather than silent drift.

### 2.1 Changes to existing tables (no duplication)

| Table | Add | Why |
|---|---|---|
| `Transactions` | `FlowType` (string, 30), `FlowSource` (`Structural/Pair/Plaid/UserRule/Keyword/Inferred/Ai/User`), `FlowConfidence` (decimal 3,2 null), `TransferPairId` (Guid null, indexed), `AuthorizedDate` (date null), `PlaidConfidence` (string null), `UserCategoryOverride` (string null), `MerchantKey` (string 100 null — normalised merchant for recurrence) | Classification layer. `Category` stays as is. |
| `Accounts` | `AvailableBalance`, `CreditLimit` (decimal null), `IsoCurrencyCode` (char 3), `OwnershipShare` (decimal 3,2 default 1), `IncludeInTotals` (bool; **true for Plaid, false for PDF until merged**), `MergedIntoAccountId` (Guid null, self-FK) | utilisation, liquidity, shared accounts, PDF separate-then-merge (`TRANSACTION_NORMALIZATION.md` §5.1) |
| `Transactions` (PDF) | `DuplicateOf` (Guid null, self-FK, indexed) | PDF row that matches a Plaid row after merge; excluded from every consumer |
| `PlaidItems` | `Products` (string, e.g. `transactions,liabilities`), `HistoryDaysRequested` (int) | know what data exists before computing a metric |

### 2.2 New tables

| Table | Key columns | Relationship / purpose |
|---|---|---|
| `AccountBalanceSnapshots` | `Id, AccountId, AsOfDate, Current, Available, Limit` unique `(AccountId, AsOfDate)` | Account 1─*. Written on each sync (one per day, last wins). Basis for net-worth trend and forecast back-tests. |
| `Liabilities` | `AccountId (PK/FK), Apr, MinimumPayment, NextPaymentDueDate, LastStatementBalance, UpdatedAt` | Account 1─1. From Plaid `/liabilities/get`. Also the "credit card due date" slice in the handoff. |
| `ClassificationRules` | `Id, UserId, MatchMerchantKey, MatchAccountId?, MatchSign?, SetFlowType?, SetCategory?, CreatedAt` | User 1─*. User corrections become rules (pipeline step 4). |
| `RecurringStreams` | `Id, UserId, AccountId, MerchantKey, Direction, Cadence, TypicalAmount, AmountTolerance, LastDate, NextExpectedDate, Status (active/ended), Source (Plaid/Detected)` | P8 #37. `Transactions.RecurringStreamId` optional FK. |
| `MonthlySummaries` | `UserId, Month (date, 1st), Income, Spending, Refunds, DebtService, SavingsContrib, InvestmentContrib, NetCashFlow, IsComplete, CalcVersion, ComputedAt` PK `(UserId, Month)` | Cache for trends, score, forecast. Rebuilt when any row in that month changes. |
| `MonthlyCategorySpend` | PK `(UserId, Month, Category)`, `Amount, Essential` | Chart (P7) and anomaly detection. |
| `HealthScores` | `Id, UserId, AsOfDate, Total, ComponentsJson (jsonb), DataCoverage (jsonb), ModelVersion` | Score history; components stored so the UI can explain the change. |
| `FinancialGoals` | `Id, UserId, Kind (emergency_fund/debt_payoff/purchase/custom), Name, TargetAmount, TargetDate?, StartAmount, LinkedAccountId?, Status, CreatedAt` | User 1─*. |
| `Forecasts` | `Id, UserId, CreatedAt, HorizonDays, Method, InputsHash, SeriesJson (jsonb: date, p10, p50, p90)` | Keep the last N for back-testing accuracy (MAPE). |
| `AiRecommendations` | `Id, UserId, CreatedAt, Model, PromptVersion, PayloadSha256, OutputJson (jsonb), Status (shown/dismissed/done)` | Audit and reproducibility. |

Not added: a separate "financial accounts" table (`Accounts` already is one) and a generic
"metrics" EAV table — typed columns on `MonthlySummaries` and JSON on `HealthScores` are easier to
query and validate. A generic `MetricSnapshots(UserId, MetricId, AsOf, Value)` table is the alternative
if the metric list grows past ~25. **Decision to argue.**

Postgres notes: `jsonb` for component and forecast series; all money `numeric(18,2)`;
indexes `(UserId, Month)` and `Transactions(UserId, FlowType, Date)`.

## 3. Services

| Service | Kind | Responsibility |
|---|---|---|
| `ClassificationService` | scoped, uses `AppDbContext` | Runs the pipeline over new/changed rows and the pair-matching window; idempotent. Called at the end of `PlaidSyncService.SyncAsync` within the same unit of work. |
| `FlowTypeRules` | static, pure | Steps 1, 3, 5 of the pipeline. Unit-tested like `TransactionCategorizer`. |
| `TransferMatcher` | static, pure | Pair matching over an in-memory window. Pure → property-based tests. |
| `MonthlySummaryBuilder` | scoped | SQL aggregation by `(month, FlowType)` → `MonthlySummaries`. |
| `CashFlowCalculator`, `LiquidityCalculator`, `DebtCalculator`, `SpendingAnalyzer` | static, pure | Formulas in `FINANCIAL_FORMULAS.md`. Return `MetricValue` with coverage. |
| `RecurringDetector` | static, pure (+ Plaid recurring adapter) | P8. |
| `ForecastService` | scoped; math in static `ForecastModels` | §6 below. |
| `HealthScoreService` | scoped; math in static `HealthScoreModel` | `FINANCIAL_HEALTH_MODEL.md`. |
| `AdvisorService` | scoped; `IAdvisorModel` interface + HTTP implementation | `AI_FINANCIAL_ADVISOR.md`. |
| `FinanceRecomputeWorker` | `BackgroundService` | Nightly per user: sync items (rate-limited), snapshot balances, rebuild changed months, score, forecast. Uses a Postgres advisory lock or `LastSyncedAt` token so the user-triggered sync and the worker cannot collide (the concurrency token already exists). |

Background processing stays **in-process** (`BackgroundService`, like `AccountEmailWorker`). Hangfire or
Quartz are not needed for one nightly job on one Pi. Plaid webhooks (`SYNC_UPDATES_AVAILABLE`) need a
public endpoint — possible after D5 (Tailscale Funnel) — and are an optimisation, not a requirement.

## 4. API endpoints

All under `[Authorize(Policy = VerifiedEmailPolicy)]`, user id from the token, as today.

| Method & route | Returns |
|---|---|
| `GET api/finance/summary?months=6` | monthly `Income/Spending/NetCashFlow/SavingsRate` + coverage (replaces rolling 30 days) |
| `GET api/finance/categories?month=2026-10` | category spend, essential flag, MoM change |
| `GET api/finance/metrics` | all current `MetricValue`s |
| `GET api/finance/net-worth?months=12` | assets, liabilities, trend from snapshots |
| `GET api/finance/health-score` / `…/history` | total, components, explanations, coverage |
| `GET api/finance/forecast?days=60` | daily p10/p50/p90 balance, expected bills, lowest point |
| `GET/POST/PUT/DELETE api/goals` | goals with progress and projected completion |
| `GET api/recurring` | streams with next expected date |
| `PATCH api/transactions/{id}` | `{ category?, flowType?, applyToMerchant: bool }` — user correction (creates a rule when `applyToMerchant`) |
| `GET api/transactions/review` | rows with `Unclassified` or low confidence, sorted by amount |
| `POST api/statements` (multipart PDF) | parsed account + row count + date range; PDF discarded after parsing |
| `GET api/finance/summary?accountId=…` | the statement view: same summary filtered to one account, included or not |
| `POST api/accounts/{id}/merge` | `{ "mode": "standalone" }` or `{ "mode": "into", "accountId": "…" }` → matched / added row counts |
| `POST api/accounts/{id}/unmerge` | sets `IncludeInTotals = false`, clears that account's `DuplicateOf` marks |
| `POST api/advisor/preview` | the exact outbound payload (P10) |
| `POST api/advisor/recommendations` | runs the advisor, returns stored recommendations (rate-limited) |

Example — `GET api/finance/health-score`:

```json
{
  "asOf": "2026-11-01",
  "total": 58,
  "band": "Coping",
  "modelVersion": "hs-1.0",
  "components": [
    { "id": "spend_vs_income", "score": 70, "weight": 0.20, "value": 0.087, "unit": "savings_rate", "explanation": "You kept 8.7% of income over the last 3 months." },
    { "id": "liquidity", "score": 52, "weight": 0.25, "value": 3.1, "unit": "months", "explanation": "Cash covers 3.1 months of essential spending." },
    { "id": "debt_burden", "score": null, "weight": 0.20, "missing": "Link your loans or enter your minimum payments." }
  ],
  "coverage": { "monthsComplete": 3, "accountsLinked": 3, "weightUsed": 0.80 }
}
```

## 5. Validation

- Query params bounded (`months` 1–24, `days` 7–90) in the controller, matching `MaxPageSize` style.
- Goals: `TargetAmount > 0`, `TargetDate > today`, `Name` ≤ 100 chars, validated in a static
  `FinanceValidation` beside `AuthValidation`.
- Calculators never throw on missing data; they return `Value = null` with a reason.
- Reconciliation check (normalisation §9 invariant 8) logged per month; a residual above
  $1 + 1 % of flows marks the month `IsComplete = false` until reviewed. **[design]**

## 6. Forecasting approach

Model choice by history length. References: Hyndman & Athanasopoulos, *Forecasting: Principles and
Practice* 3rd ed. (free online) — details and verification in `ACADEMIC_RESEARCH.md`.

| History | Recurring items | Variable spend per category | Uncertainty |
|---|---|---|---|
| < 3 complete months | calendar projection of detected streams only | median of available weeks × 4.35 | wide fixed band (±30 %) and "early estimate" label |
| 3–5 months | streams | mean or median (robust to one spike) | empirical quantiles of monthly residuals |
| 6–11 months | streams | simple exponential smoothing (α by SSE grid 0.1–0.9) or weighted MA | SES prediction interval √(1 + (h−1)α²)·σ |
| ≥ 12 months | streams | SES; add a seasonal index per month only with ≥ 24 months (two full seasons) | same, plus seasonal |
| ≥ 24 months | streams | Holt-Winters / ETS(A,N,A) per category, compared with seasonal naive on a hold-out | model intervals |

Why not ARIMA or ML: monthly personal series are short (3–36 points), noisy and full of structural
breaks (moves, job changes). Complex models over-fit them and are hard to explain; FPP3 itself shows
simple methods are often competitive benchmarks. Personal-finance forecasts are mostly **calendar
arithmetic over known recurring items**, with a small statistical layer for discretionary spending.
**[design]** Daily balance simulation: start from current balance (minus pending), add stream events on
their dates, spread variable spend evenly per day, report p10/p50/p90. Irregular expenses
(annual insurance, car repairs) use Croston-style "average size ÷ average interval" accruals.

Two caveats from the sources (Part C §G): FPP3 rejects fixed minimum-history rules ("no
justification for the magic number of 30"; the only limit is more observations than parameters), so
the thresholds above are **[design]** for interpretability. The 24-month rule for seasonal models is a
hard constraint in R's `HoltWinters`, `decompose` and `stl` (need ≥ 2 periods), not a textbook rule.
Croston forecasts are known to be biased and have no analytic prediction intervals — use them for
accruals, not for the uncertainty band.

All in C# with `decimal`/`double` and no library. Back-test every forecast against what happened
(stored in `Forecasts`) and show accuracy once there are three months of results.

## 7. Testing strategy

| Layer | Tool | What |
|---|---|---|
| Pure calculators, matcher, score, forecast models | xUnit facts/theories | Every formula with the worked examples in these docs as golden values; edge cases: zero income, one month, negative balances, `P ≤ rB` |
| Classification invariants | xUnit with generated ledgers | invariants 1–8 in `TRANSACTION_NORMALIZATION.md` §9 |
| Endpoints | `WebApplicationFactory` + InMemory (existing `AuthWebApplicationFactory`) | user scoping, coverage flags, 401/403 |
| Plaid | existing fake `IPlaidClient` | pair matching across two fake items; re-link duplicate detection |
| AI | fake `IAdvisorModel` returning bad numbers / extra candidates | validator rejects; fallback to templates |
| Postgres-specific SQL | (gap) InMemory does not translate `date_trunc` or `jsonb` the same way | add a Testcontainers Postgres suite later — **new dependency, needs approval** |

Project rule applies: each new test must fail before it passes.
