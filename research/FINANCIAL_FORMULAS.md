# Financial Calculation Specification

Engineering specification. Formulas are standard household-finance definitions; where a threshold
or benchmark comes from research it is cited by key to `ACADEMIC_RESEARCH.md` (e.g. **[Greninger96]**)
or `INSTITUTIONAL` notes in that file. Everything else is labelled **[design]** — a product choice
that can be argued with.

## 0. Conventions every formula assumes

- **Ledger sign**: `Amount < 0` is money out of the user's household, `> 0` money in.
- **Flow type** (from `TRANSACTION_NORMALIZATION.md`): each row has exactly one of
  `Income, Expense, Refund, InternalTransfer, DebtPayment, SavingsContribution,
  InvestmentContribution, InvestmentWithdrawal, LoanProceeds, ExternalTransferIn, Unclassified`.
  **All formulas below consume flow type, never raw category or raw sign.**
- **Period**: calendar month `m` in the user's time zone. Rolling windows only where stated.
- **Complete month**: a month is *complete* only if every linked account has transaction coverage for
  the whole month (account linked before the month began and synced after it ended). Incomplete months
  are excluded from averages and flagged. **[design]**
- **Data source tier** (shown per metric): **T** = transactions only; **B** = needs balances;
  **L** = needs Plaid Liabilities; **I** = needs Plaid Investments; **U** = needs user input.
- Money is `decimal`; ratios are `decimal` rounded only for display. Division by zero returns `null`,
  never 0 (a 0 % savings rate and "unknown" are different facts).

Notation: `Inc_m`, `Exp_m` (positive numbers), `Ref_m` refunds, `Sav_m` net transfers to savings,
`Inv_m` net investment contributions, `DebtPmt_m`, `Bal_a(t)` balance of account `a` at time `t`.

---

## 1. Cash flow

| Metric | Formula | Tier | Example | Interpretation / limits |
|---|---|---|---|---|
| **Net income (take-home)** | `Inc_m = Σ Amount where flow = Income` | T | payroll 2×1,850 + interest 4 = 3,704 | Bank data sees **net** pay only. |
| **Gross income** | user-entered, or `Inc_m / (1 − t̂)` with user-confirmed tax rate | U | 3,704 / (1−0.22) ≈ 4,749 | **Cannot** be derived from bank data; never guess silently. Needed for lender-style DTI. |
| **Expenses (consumption)** | `Exp_m = −Σ Amount where flow ∈ {Expense}` − `Σ Refund` | T | 2,900 spend − 120 refunds = 2,780 | Card purchases count on purchase date; the later card payment does not. |
| **Debt service** | `DebtPmt_m = −Σ Amount where flow = DebtPayment` (non-card loans) + card *interest & fees* | T (L for split) | car 420 + student 180 = 600 | Principal is not consumption but is a cash obligation. Card **payments** are excluded (already counted as purchases). |
| **Net cash flow** | `NCF_m = Inc_m − Exp_m − DebtPmt_m` | T | 3,704 − 2,780 − 600 = 324 | What the current branch labels "Savings". |
| **Savings rate (cash-flow basis)** | `SR_m = NCF_m / Inc_m` | T | 324 / 3,704 = 8.7 % | Delphi panel median benchmark: savings ≥ 10 % of income, for both gross and after-tax income **[Greninger96, Table 1]**. Ours is after-tax; say so in the UI. |
| **Savings rate (behavioural basis)** | `SRb_m = (Sav_m + Inv_m) / Inc_m` | T | (300 + 200)/3,704 = 13.5 % | Money the user *deliberately moved*. Differs from SR when savings are later withdrawn. Report both; score uses a 3-month mean of SR. **[design]** |
| **Expense-to-income** | `EIR_m = (Exp_m + DebtPmt_m) / Inc_m` | T | 3,380/3,704 = 0.91 | > 1 means spending exceeds income — "spend less than income" is FinHealth indicator 1 **[FHN]**. Total expenses / after-tax income: 80 % "reasonable", 90 % "danger point" (panel medians, **[Greninger96]**). Greninger's expenses include debt payments, as here. |
| **Income volatility** | `CV_inc = σ(Inc_m) / μ(Inc_m)` over the last *n* complete months, n ≥ 6 | T | incomes 3,700, 3,700, 5,550, 3,700, 3,700, 3,700 → μ 4,008, σ 755 → CV 0.19 | CV is sensitive to 3-paycheck months; compute on **monthly-equivalent** pay (§1a) or use median absolute deviation. Large swings are normal: 55 % of 1 million Chase customers had a > 30 % month-to-month income change **[JPMCI-volatility]** (2016 report), so do not penalise moderate CV harshly. |
| **Cash-flow trend** | OLS slope of `NCF_m` on `m` over 6–12 complete months; report slope and *n* | T | slope +35 / month | With < 6 points the slope is noise; show "not enough history". |

### 1a. Normalising pay frequency **[design]**
Bi-weekly pay produces 26 cheques/year → two months a year with three. For volatility and forecasts,
compute *monthly-equivalent income* per recurring income stream:
`MEI = median(cheque) × periods_per_year / 12` (weekly 52, bi-weekly 26, semi-monthly 24, monthly 12).
Use actual cash for cash-flow history, MEI for "expected income".

---

## 2. Savings and liquidity

| Metric | Formula | Tier | Example | Interpretation / limits |
|---|---|---|---|---|
| **Liquid assets** | `LA = Σ Bal_a` for `a.type = depository` (checking, savings, money market, cash management) | B | 1,200 + 4,800 = 6,000 | Use `available` where present for checking, `current` for savings. Excludes brokerage (not liquid within days without loss). |
| **Essential monthly expenses** | `EssExp = mean over 3–6 complete months of Exp in essential categories + DebtPmt` | T | 1,950 | Essential mapping in §4. |
| **Emergency-fund coverage (months)** | `EFC = LA / EssExp` | B+T | 6,000/1,950 = 3.1 | Liquid assets ÷ monthly expenses: panel median 300 % (3 months), summary recommendation ≥ 250 % **[Greninger96]**; their "monthly expenses" include debt payments, taxes and set-asides for irregular costs. Greninger's liquid assets = cash, checking, savings, money market, CDs ≤ 6 months — the same set as `LA`. Using *total* expenses instead of essential is more conservative; show which. |
| **Liquid buffer (days)** | `LA / (Exp_m + DebtPmt_m) × 30.4` | B+T | 6,000/3,380×30.4 = 54 days | Easier to read than months. JPMorgan Chase Institute (*Weathering Volatility 2.0*, 2019, 6 million families' bank data): a simultaneous income dip and spending spike needs a median **6.2 weeks** of take-home pay; 65 % of families lack it **[JPMCI-buffer]**. A bank-data-derived target, unlike survey benchmarks. "3–6 months of living expenses" is FINRA's wording; the CFPB gives no month target. |
| **Goal progress** | `p = (Saved_g − Start_g) / (Target_g − Start_g)` | U+B | (2,400−0)/6,000 = 40 % | Needs a goal → account mapping, else "saved" is unknowable. |
| **Required monthly saving** | No growth: `PMT = (Target − Saved) / n`. With monthly rate `r`: `PMT = (Target − Saved(1+r)^n) · r / ((1+r)^n − 1)` | U | 3,600 left, 12 months, r = 0.04/12 → 294.54 | `r` is the account APY / 12, user-visible. |
| **Projected completion date** | smallest `n` with `Saved(1+r)^n + C·((1+r)^n−1)/r ≥ Target`, `C` = trailing 3-month mean of net contributions | U+T | 3,600 / 300 → 12 months | If `C ≤ 0` → "not on track", never a date. |
| **Savings consistency** | share of the last 6 complete months with `NCF_m > 0` (or `Sav_m > 0`) | T | 5/6 = 83 % | Behavioural signal, robust to amounts. |
| **Savings-rate trend** | slope of `SR_m` over 6–12 months | T | +0.4 pp / month | Same caveats as cash-flow trend. |

---

## 3. Debt

| Metric | Formula | Tier | Example | Interpretation / limits |
|---|---|---|---|---|
| **Total debt** | `D = Σ Bal_a` for `a.type ∈ {credit, loan}` (Plaid reports owed as positive) | B | card 1,400 + auto 9,800 = 11,200 | Store debt as positive liability, never mix signs with assets. |
| **Debt-to-income (lender style)** | `DTI = monthly required debt payments / gross monthly income` | L+U | (35 + 420 + 180)/4,749 = 13.4 % | CFPB defines DTI this way but sets no threshold; the 43 % General-QM DTI limit was **removed** by the 2020 rule (effective 1 Mar 2021) in favour of a price-based test. "28/36" has no primary source found. Requires **gross** income and **minimum** payments. |
| **Debt-service ratio (cash basis)** | `DSR = DebtPmt_m / Inc_m` (loan payments + card interest and fees; with Liabilities, add card **minimum** payments instead of interest) | T (L better) | 600/3,704 = 16.2 % | Card payments that clear a statement in full repay purchases already counted as spending, so they are not debt service. Uses net pay, which matches the benchmark: total debt payments / after-tax income 35 % "reasonable", 45 % "danger point"; non-mortgage debt payments 10 % (median; summary table ≤ 15 %) reasonable, 20 % danger **[Greninger96]**. |
| **Credit utilisation** | `U = Σ card balances / Σ card limits` (also per card) | B (limit) | 1,400/5,000 = 28 % | Needs `balances.limit` — currently discarded. "Below 30 %" is a rule of thumb: myFICO says the data does not show a score dip at 30 % and lower is better; Experian calls 30 % where the effect becomes more pronounced and reports 800–850 scorers average 7.1 %. No CFPB source found. |
| **Monthly interest cost** | `I = Σ_debts Bal × APR/12` | L | 1,400×0.2499/12 = 29.16 | Approximation; card interest actually uses average daily balance and grace periods. Transactions show actual charges (`BANK_FEES_INTEREST_CHARGE`, in both taxonomy versions) — prefer actuals; Liabilities also returns `interest_charge_amount` per APR. |
| **Payoff months (fixed payment P)** | `n = −ln(1 − r·B/P) / ln(1 + r)`, `r = APR/12`; undefined if `P ≤ r·B` | L | B 1,400, APR 24.99 %, P 100 → n ≈ 16.7 | If `P ≤ r·B` the debt never amortises — a critical alert. |
| **Total interest on payoff** | `n·P − B` | L | 16.72×100 − 1,400 ≈ 272 | |
| **Avalanche vs snowball** | simulate month by month: pay minimums on all, extra `E` to highest APR (avalanche) or smallest balance (snowball) | L+U | report months-to-free and total interest for both | Avalanche minimises interest mathematically; snowball has behavioural support **[GalMcShane12]**, and people often spread repayment sub-optimally **[Gathergood19]**. Show both with the cost difference. |

---

## 4. Spending behaviour

| Metric | Formula | Tier | Notes |
|---|---|---|---|
| **Spending by category** | `Exp_m,c = −Σ Amount (Expense) + Σ Refund`, grouped by category `c` | T | Refunds net against their own category, floored at 0 per month. |
| **Essential vs discretionary** | map Plaid detailed codes: essential = `RENT_AND_UTILITIES_*`, `FOOD_AND_DRINK_GROCERIES`, `MEDICAL_*`, `TRANSPORTATION_GAS`/`_PUBLIC_TRANSIT`, insurance (`GENERAL_SERVICES_INSURANCE`), childcare/education, loan minimums; rest discretionary | T | **[design]**; mapping is a table, user-overridable. Mirrors 50/30/20 needs/wants split (origin cited in research file) but not its percentages. |
| **Recurring expenses** | group by normalised merchant; stream if ≥ 3 occurrences with interval CV < 0.25 and amount within ±10 % (or ±$2) **[design]**; cadence = median interval snapped to {7, 14, 15, 30, 91, 365} | T | Plaid `/transactions/recurring/get` already does this — evaluate it first (P8, #37). |
| **Subscriptions** | recurring stream with monthly/annual cadence, amount < $100 **[design]**, category entertainment/software/general services | T | Flag price increases: last amount > previous by > 5 %. |
| **Month-over-month change** | `Δ_c = (Exp_m,c − Exp_m−1,c) / Exp_m−1,c`; show absolute $ as well | T | Percentages on small bases mislead (from $5 to $20 is +300 %). Suppress when base < $50. **[design]** |
| **Unusual spending** | robust z: `z = (x − median_c) / (1.4826·MAD_c)` per category over ≥ 6 months; flag `|z| > 3.5` | T | MAD-based outlier rule (Iglewicz & Hoaglin threshold 3.5). Single-transaction anomalies: amount > 3× category median **and** > $100. |
| **Lifestyle inflation** | compare 6-month means: `ΔExp% − ΔInc%` (discretionary only) | T | Needs ≥ 12 months to separate from seasonality. |

---

## 5. Wealth

| Metric | Formula | Tier | Notes |
|---|---|---|---|
| **Net worth** | `NW = Σ assets − Σ liabilities`, assets = depository + investment (+ user-entered property, vehicles), liabilities = credit + loan | B (+I, U) | Without investments and home, NW is "**linked** net worth"; always label coverage. |
| **Asset / liability breakdown** | group by Plaid type/subtype | B | |
| **Cash reserves** | `LA` (§2) | B | |
| **Investment allocation** | holdings × security type → equity / fixed income / cash / other | I | Needs Plaid Investments (`/investments/holdings/get`). |
| **Net-worth trend** | from daily/monthly **balance snapshots** | B | Not derivable retroactively from transactions unless balances are back-cast: `Bal(t−1) = Bal(t) − Σ Amount on t` — valid only for accounts with complete history. |

---

## 6. Forecasting (formulas; model choice in `TECHNICAL_ARCHITECTURE.md` §6)

| Metric | Formula | Tier |
|---|---|---|
| **Expected income** | Σ over active income streams of next expected dates × MEI-derived amount; irregular income = trailing 6-month 25th percentile (conservative) **[design]** | T |
| **Expected recurring expenses** | Σ active outflow streams with next due date = last date + cadence | T |
| **Variable expense forecast** | per category, model selected by history length (3 mo: mean/median; 6 mo: SES; ≥ 24 mo: seasonal ETS) **[Hyndman]** | T |
| **Projected balance** | `Bal(t+h) = Bal(t) + Σ expected inflows − Σ expected outflows` day by day, with an 80 % band from the variable-spend residuals | B+T |
| **Lowest projected balance / overdraft risk** | `min_h Bal(t+h)`; alert if < 0 or < one week of spending | B+T |
| **Emergency-fund projection** | `LA(t+n) = LA(t) + n·C` vs target `k·EssExp` → date reached | B+T |
| **Stress scenarios** | re-run projection with (a) income −100 % for 3 months, (b) a $400 / $2,000 shock, (c) +20 % variable spend; report months of runway | B+T |

The $400 shock scenario matches the Federal Reserve SHED question: in the 2026 report (survey fielded
October 2025), 63 % of adults would cover a $400 expense with cash or its equivalent (Part C §C of
`ACADEMIC_RESEARCH.md`).

---

## 7. C# implementation notes

- One static, pure class per area (`CashFlowCalculator`, `LiquidityCalculator`, `DebtCalculator`,
  `SpendingAnalyzer`, `ForecastEngine`) taking **already-loaded, already-classified** rows. No `DbContext`
  inside calculators → trivially unit-testable, matches `TransactionCategorizer`'s existing static style.
- Aggregate in SQL (`GroupBy(month, flowType)`), compute ratios in C#. Postgres `date_trunc('month', …)`
  via `new DateOnly(t.Date.Year, t.Date.Month, 1)` translates in Npgsql.
- `decimal` everywhere; `Math.Log`/`Math.Pow` need `double` — convert at the boundary and round results
  to cents with `MidpointRounding.ToEven`.
- Return `MetricValue(decimal? Value, string Unit, int MonthsUsed, string[] Inputs, string? Caveat)`
  so the UI and the AI layer both see *coverage* and *assumptions*, not a bare number.

## 8. What bank data cannot tell you

Gross income and taxes; employer retirement contributions deducted before pay; cash spending after
withdrawal; accounts the user has not linked (partial picture → every ratio is biased); household
members' accounts; assets such as property; whether a transfer to a person is a gift, loan or bill
share. The UI must show **coverage** ("3 of your accounts, 87 days") next to every derived figure.
