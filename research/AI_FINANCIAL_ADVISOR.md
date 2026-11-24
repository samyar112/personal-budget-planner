# AI Financial Advisor — Design

**Core rule (already decided, 2026-11-16):** the LLM never computes a metric or the score. A
deterministic engine produces numbers; the LLM *selects, prioritises and explains* actions over those
numbers. Outbound payload is **allowlist-built**: typed numbers and our own category codes only, no
free text, no merchant names, no masks, no balances tied to institutions (#34 P9).

This document extends that decision; nothing here reopens it.

---

## 1. Pipeline

```
Ledger ──► Classifier ──► Metric engine ──► Recommendation candidates (rules) ──► Payload builder ──► LLM
 (rows)    (FlowType)     (FINANCIAL_FORMULAS)   (deterministic, each with           (allowlist +         │
                                                  evidence ids)                       PII validator)      ▼
                                                                                              Output validator
                                                                                                    │
                          UI ◄── stored AiRecommendation (payload hash, model, prompt version) ◄────┘
```

**Why candidates come from rules, not the LLM.** Rules can only propose actions whose arithmetic
is already done ("cut dining by $X gets you to a 3-month buffer by date D"). The LLM ranks them,
writes the explanation for *this* user, and may decline candidates that conflict. This makes every
recommendation traceable to a metric id and an evidence row set, which is the brief's requirement.

### Candidate rules (initial set) **[design]**

| Id | Trigger | Action computed by engine |
|---|---|---|
| `EF_BUILD` | `EFC < 3` | monthly amount to reach 1 month / 3 months by a date; source = top discretionary categories |
| `NEG_CASHFLOW` | 3-month mean `NCF < 0` | gap per month; categories whose cut closes it |
| `CARD_INTEREST` | card interest charges > 0 in last 90 days | interest paid; avalanche order; months saved with +$E |
| `NEVER_AMORTISES` | `P ≤ r·B` on any debt | minimum extra payment to amortise in 36 months |
| `HIGH_UTIL` | utilisation > 30 % (needs limit) | paydown to reach 30 % |
| `SUB_REVIEW` | subscriptions total > X or price rise detected | list of streams (category + amount only) |
| `CATEGORY_SPIKE` | robust z > 3.5 in a category | amount above typical |
| `GOAL_OFF_TRACK` | projected date > target date | required monthly saving vs current |
| `OVERDRAFT_RISK` | forecast min balance < 0 within 30 days | date and shortfall |
| `SURPLUS_IDLE` | `EFC > 6` and positive `NCF` for 6 months | amount above 6-month buffer (no product recommendation) |

---

## 2. Input contract (what leaves the API)

JSON Schema enforced by the payload builder *and* re-validated by a fail-closed validator (regex for
digits ≥ 6 in a row, emails, URLs, names list; any hit aborts the call).

```json
{
  "schemaVersion": "1.0",
  "asOf": "2026-11-01",
  "coverage": { "linkedAccountTypes": ["checking", "savings", "credit card"], "monthsComplete": 3, "hasLiabilities": false, "hasInvestments": false },
  "metrics": [
    { "id": "income_monthly_mean", "value": 3704.00, "unit": "USD", "months": 3 },
    { "id": "spending_monthly_mean", "value": 2780.00, "unit": "USD", "months": 3 },
    { "id": "net_cash_flow_mean", "value": 324.00, "unit": "USD", "months": 3 },
    { "id": "savings_rate", "value": 0.087, "unit": "ratio", "months": 3 },
    { "id": "emergency_fund_months", "value": 3.1, "unit": "months" },
    { "id": "income_cv", "value": null, "unit": "ratio", "caveat": "needs 6 complete months" }
  ],
  "categories": [
    { "code": "FOOD_AND_DRINK", "essential": false, "monthlyMean": 540.00, "prev3Mean": 410.00 },
    { "code": "RENT_AND_UTILITIES", "essential": true, "monthlyMean": 1250.00, "prev3Mean": 1250.00 }
  ],
  "healthScore": { "total": 58, "components": [ { "id": "liquidity", "score": 52, "weight": 0.25 } ] },
  "candidates": [
    {
      "id": "EF_BUILD",
      "evidence": ["emergency_fund_months", "spending_monthly_mean"],
      "computed": { "targetMonths": 3, "monthlyAmount": 150.00, "reachDate": "2027-06-01", "fundFrom": ["FOOD_AND_DRINK"] }
    }
  ],
  "userGoals": [ { "kind": "emergency_fund", "target": 6000.00, "targetDate": "2027-06-30" } ]
}
```

Built only from accounts with `IncludeInTotals = true` and rows with `DuplicateOf` null, so an unmerged
PDF statement never reaches the model (`TRANSACTION_NORMALIZATION.md` §5.1).
No institution, account name, mask, merchant, description, date of an individual transaction or
absolute balance per account. Aggregate liquid assets appear only inside derived metrics. **[design]**

## 3. Output contract (what the model must return)

Enforced with the provider's **structured output / JSON-schema mode**, then re-validated in C#.

```json
{
  "recommendations": [
    {
      "candidateId": "EF_BUILD",
      "priority": 1,
      "title": "Build a three-month cash buffer",
      "explanation": "Your cash covers about 3.1 months of spending. Setting aside $150 a month reaches your $6,000 goal by June 2027.",
      "numbersUsed": [ { "metricId": "emergency_fund_months", "value": 3.1 }, { "candidateField": "monthlyAmount", "value": 150.00 } ],
      "assumptions": ["Spending stays near the last 3 months' average."],
      "confidence": "medium"
    }
  ],
  "dataGaps": ["Debt interest rates are unknown, so debt advice is limited."],
  "disclaimerShown": true
}
```

Constraints: 3–5 recommendations (#36); `candidateId` ∈ input candidates; `explanation` ≤ 300 chars.

## 4. Validation after the model replies (fail closed)

1. JSON parses and matches the schema; `candidateId` exists in the payload.
2. **Number check:** every number in `explanation` (regex for currency, percentages, decimals, dates) must
   equal — after rounding to the displayed precision — a value in `numbersUsed`, and each `numbersUsed`
   entry must equal a value in the input. Any unmatched number → reject that recommendation.
3. Prohibited content: specific securities, tickers, insurance or loan products, tax/legal claims,
   guarantees ("will", "guaranteed"), and urgency language. Keyword list + reject.
4. If fewer than 3 survive, fall back to **template text for the top rule candidates**. The feature
   degrades to rule-based, never to unvalidated text.

## 5. Prompt (system, version-controlled as `prompts/advisor.v1.md`)

```
You explain a person's finances using ONLY the JSON you are given.
- Use only numbers that appear in the input. Do not calculate new figures, totals, or percentages.
- Choose 3 to 5 items from "candidates". Do not invent actions that are not candidates.
- Rank by: avoiding overdraft or debt that never pays down first, then emergency savings,
  then high-interest debt, then everything else.
- When "coverage" shows missing data, say what is missing in "dataGaps" instead of guessing.
- Plain language, second person, no jargon, no product or investment recommendations,
  no tax or legal advice. You are not a licensed financial adviser; do not claim to be.
- Return JSON matching the schema exactly.
```

The ranking order mirrors the hierarchy the research supports — liquidity shortfalls predict
financial fragility **[Lusardi-fragile]**, and high-cost debt is the most certain "return" — but the
exact order is a **[design]** choice.

Few-shot examples live in the same file; one example must show a refusal to give advice outside the
candidates.

## 6. Other AI uses

| Use | Allowed input | Deterministic guard |
|---|---|---|
| Category for `OTHER` rows (P6 parked note) | cleaned merchant token string, sign, account type | output ∈ category enum; confidence; user can correct |
| Anomaly explanation | category, typical vs current amount, z-score | template if validator fails |
| Scenario narration ("what if I lose income for 3 months") | engine's scenario output only | same number check |
| Free-form chat | **not recommended** for this release: it requires free text in both directions and breaks the allowlist decision |

## 7. Provider, cost and privacy

- The project has **no AI provider today.** Any provider works behind `IAdvisorModel` (same pattern as
  `IPlaidClient`), keeping the test suite offline with a fake.
- Recommended default: Anthropic Claude via the Messages API with `output_config.format` (JSON schema
  structured outputs). The official .NET package is `Anthropic` on NuGet; using it is a **new project
  dependency (needs approval)**. The decision on record is a thin `HttpClient`, which works without one.
- Cost: one call per user per refresh (monthly, or on demand with a rate limit). Payload ≈ 1–2 K tokens.
  At current list prices that is fractions of a cent per call on a small model; cache the system prompt.
- Free alternative: a local model via Ollama on a separate machine. Not on the Pi (RAM). Quality of JSON
  adherence is lower, so the validator matters more.
- Store `AiRecommendation` rows with `PayloadSha256`, `PromptVersion`, `Model`, `CreatedAt` so any tip
  can be reproduced and audited; store the payload itself only if the privacy preview (#35) showed it.

## 8. Responsible-AI requirements

- Show the exact payload before sending (P10, #35) — the strongest trust feature the product has.
- Label every tip "generated from your data on {date}", show `numbersUsed` on expand.
- Disclaimer: educational, not individualised financial, tax or legal advice (regulatory notes in
  `ACADEMIC_RESEARCH.md` §Institutional).
- Do not generate recommendations when `monthsComplete < 2`; show "connect more history".
- Evaluate: a fixed set of ~30 synthetic payloads (thin file, negative cash flow, high debt, surplus)
  with expected candidate selections; run on every prompt change; fail on any unmatched number.
