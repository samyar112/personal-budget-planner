# Financial Health Score — Model `hs-1.0`

**What it is:** an explainable 0–100 score of *observed financial behaviour and position*, built only
from linked-account data, for issue #38 (P12, "rule-based").
**What it is not:** it is not the CFPB Financial Well-Being Scale (a 10- or 5-item **survey** of
perceptions) and not the Financial Health Network's FinHealth Score (an 8-indicator **survey**). It borrows
their structure and published benchmarks; its weights are product-design choices. The UI must say so.

Sources by key are in `ACADEMIC_RESEARCH.md`.

---

## 1. What research supports, and what it does not

| Claim used in this model | Evidence | Status |
|---|---|---|
| Bank-record measures (balances, net spending, payment problems) carry financial-well-being information comparable to self-reports | **[ComertonForde22]** builds an "Observed" IRT scale from exactly those inputs | research-supported (abstract level) |
| Objective transaction indicators can be combined into a customer-level index: savings/income, emergency-fund coverage, debt service/income, card utilisation, income and expense variability, basic vs non-basic spend | **[AlvarezFranco26]** (432,695 bank clients; PCA weights; 70 % objective / 30 % subjective) | research-supported |
| Liquidity is the strongest single predictor of household insolvency; debt payments/income is second | **[DeVaney94]**, SCF 1983→1986, n = 1,934 | research-supported, old data |
| Benchmarks: liquid assets ≥ 2.5–3 months of expenses; savings ≥ 10 % of income; total debt payments/after-tax income 35 % reasonable, 45 % danger; total expenses/after-tax income 80 % reasonable, 90 % danger | **[Greninger96]** Delphi panel, n = 156 (full text, Table 1) | expert consensus, not outcome-validated |
| Perceived well-being has a *present* (money-management stress) and a *future* (security) dimension | **[Netemeyer18]** | research-supported; used for grouping only |
| Survey scores and objective ratios agree on direction but diverge for individuals | **[Khashadourian24]**, **[Tsaban26, preprint]** | supports *not* calling this a well-being score |
| Equal weighting of indicators | FinHealth Score methodology **[FHN]** | institutional practice, not validated optimum |
| The specific weights below | — | **[design]**, to be calibrated |

**No published study gives validated weights for a transaction-only score of this kind in US data.**
Alvarez-Franco et al. derive weights by PCA on one Colombian bank's clients; their weights do not transfer.
So: weights here are a transparent prior, published in the UI, and the plan (§7) is to calibrate them.

## 2. Dimensions

Two groups, following the present/future split in **[Netemeyer18]**:

| # | Pillar | Group | Weight | Metric (from `FINANCIAL_FORMULAS.md`) | Needs |
|---|---|---|---|---|---|
| 1 | Spend less than income | present | 0.20 | 3-month mean cash-flow savings rate `SR` | T |
| 2 | Liquidity buffer | future | 0.25 | `LA ÷ mean monthly (spending + debt service)` in months | B+T |
| 3 | Debt burden | present | 0.20 | ½ DSR score + ½ utilisation score (DSR only if no limits) | T (+B limit) |
| 4 | Bills and fees | present | 0.15 | count of overdraft, NSF and late-fee rows, last 6 months | T |
| 5 | Income stability | future | 0.10 | CV of monthly-equivalent income, ≥ 6 complete months | T |
| 6 | Saving habit | future | 0.10 | share of complete months (up to 6) with `NCF > 0` or a savings/investment contribution | T |

Why these weights **[design]**: liquidity highest because it is the best-supported predictor
**[DeVaney94]** and the core of fragility measures **[LusardiFragile11]**; cash flow and debt next because
both appear in every framework reviewed; fees are a direct observed-distress signal **[ComertonForde22]**;
stability and habit lowest because they need the longest history and are noisiest at 6 months.
Group totals: present 0.55, future 0.45.

Not included (data we do not have): insurance adequacy, credit score, long-term/retirement savings, planning
behaviour — four of the FinHealth indicators. Investment and retirement balances can be added as a
pillar once Plaid Investments is linked.

## 3. Scoring functions

Piecewise-linear interpolation between anchor points; clamp to [0, 100]. Linear pieces are easy to
explain ("each extra percent of income saved adds 4 points up to 10 %") and monotone by construction.

| Pillar | Anchors (input → points) | Anchor source |
|---|---|---|
| 1 `SR` | −10 % → 0; 0 % → 40; 10 % → 80; ≥ 20 % → 100 | 10 % = **[Greninger96]** median; others **[design]** |
| 2 months | 0 → 0; 1 → 40; 2.5 → 75; 3 → 80; ≥ 6 → 100 | 2.5 and 3 from **[Greninger96]**; 6 = upper end of FINRA's "three to six months"; rest **[design]**. JPMCI's 6.2-week buffer is measured in take-home *pay*, not expenses, so it is not used as an anchor |
| 3a DSR (debt payments ÷ after-tax income) | 0 % → 100; 35 % → 60; 45 % → 20; ≥ 60 % → 0 | 35 / 45 = **[Greninger96]** reasonable / danger; rest **[design]** |
| 3b utilisation | ≤ 10 % → 100; 30 % → 70; 50 % → 40; ≥ 90 % → 0 | 30 % = widely used guidance (see `ACADEMIC_RESEARCH.md` institutional notes); **[design]** |
| 4 fee events / 6 months | 0 → 100; 1 → 70; 2 → 50; 3–4 → 25; ≥ 5 → 0 | **[design]** |
| 5 CV income | ≤ 0.05 → 100; 0.15 → 75; 0.30 → 40; ≥ 0.50 → 0 | **[design]** |
| 6 share of months | 0 → 0; 1 → 100 (linear) | **[design]** |

Total: `Score = round( Σ wᵢ·sᵢ / Σ wᵢ )` over **available** pillars.

Bands (display only): 80–100 *Healthy*, 40–79 *Coping*, 0–39 *Vulnerable* — the FinHealth tier
names and cut-offs **[FHN]**, used because they are recognised; the label must still say the score is
our own.

## 4. Missing data

Scope: only accounts with `IncludeInTotals = true` count; an unmerged PDF statement does not change the
score until the user merges it.

| Situation | Rule |
|---|---|
| Pillar input unavailable (e.g. no balances, < 6 months for stability) | drop the pillar, re-normalise weights, list it under "not scored" with what would unlock it |
| Available weight `Σ wᵢ < 0.60` | no total score; show pillars only ("Provisional — connect more history") |
| Fewer than 2 complete months | no score at all |
| Utilisation unknown | pillar 3 = DSR score alone |
| Zero income in window | pillar 1 = 0 if spending > 0; pillar 3 DSR = 0 if any debt payments, else 100; flag "no income detected" so a user paid in cash or into an unlinked account understands why |
| Partial account coverage (user says some accounts are unlinked) | score still computed; banner "based on 3 linked accounts" — never silently extrapolate |

Never impute a value (e.g. a median user) — an imputed number is a fabricated fact about this person.

## 5. Worked example (the user in `FINANCIAL_FORMULAS.md`, 3 complete months)

| Pillar | Input | Points | Weight |
|---|---|---|---|
| 1 | SR 8.7 % | 40 + 8.7×4 = **74.8** | 0.20 |
| 2 | 6,000 ÷ 3,380 = 1.78 months | 40 + (0.78/1.5)×35 = **58.1** | 0.25 |
| 3 | DSR 16.2 % → 81.5; utilisation 28 % → 73.0 | ½(81.5 + 73.0) = **77.2** | 0.20 |
| 4 | 1 overdraft fee | **70** | 0.15 |
| 5 | 3 months only | *not scored* | (0.10) |
| 6 | 2 of 3 months positive | **66.7** | 0.10 |

`(0.20·74.8 + 0.25·58.1 + 0.20·77.2 + 0.15·70 + 0.10·66.7) / 0.90 = 69.0` → **69, Coping**, with
"Income stability: needs 3 more months" listed.

Explanation shown to the user is generated from the table, not the LLM: *"Your biggest lever is your cash
buffer: 1.8 months of spending. Reaching 3 months would add about 6 points."* (0.25 × (80 − 58.1) / 0.90 = 6.1; the number in the
sentence is computed by code, never written by the LLM.)

## 6. Properties to test

- **Monotone:** more savings, more liquidity, less debt service, fewer fees never lower the score.
- **Bounded:** 0 ≤ score ≤ 100 for any input including negative income and zero balances.
- **Responsive (issue #38 outcome):** a 10 % cut in spending changes pillar 1 (and the total) — test it.
- **Explainable:** Σ of displayed contributions = total (± rounding).
- **Stable:** a single large refund or one-off bonus moves the score by < 5 points (use 3-month means). **[design]**

## 7. Calibration plan (after release)

1. Optional in-app CFPB 5-item scale (public-domain instrument, scored with CFPB's published conversion
   table, see institutional notes) collected with consent.
2. Correlate pillar scores with the CFPB score; expect moderate correlation **[ComertonForde22]**,
   **[Khashadourian24]** — a low correlation does not invalidate an *observed-behaviour* score.
3. Better target: future distress (overdrafts, missed payments in the next 6 months). Fit a monotone
   logistic model on pillar scores and replace hand weights with fitted ones, keeping them published.
4. Version every change (`hs-1.1`) and store `ModelVersion` with each score.
