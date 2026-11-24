# Academic and Institutional Research

All metadata and citation counts checked **2026-11-24** against OpenAlex, Semantic Scholar and Crossref
(free APIs). Citation counts differ by source and neither is authoritative. Every finding is labelled
**[abstract]**, **[full text]** or **not verified**; nothing was filled in from memory. Where the brief
named an author or DOI that turned out to be wrong, the entry says so.

## Citation keys used in the other documents

| Key | Entry | Key | Entry |
|---|---|---|---|
| AlvarezFranco26 | A1 | Gelman14 | B1 |
| ComertonForde22 | A2 | Baker18 | B2 |
| Khashadourian24 | A3 | GanongNoel19 | B3 |
| YeoLimYii23 | A4 | OlafssonPagel18 | B4 |
| Gomes21 | A5 | JPMCI-volatility | B5, C-E |
| LusardiMitchell11 | A6 | Netemeyer18 | B7 |
| Greninger96 | B9 | DeVaney94 | B10 |
| LusardiFragile11 / Lusardi-fragile | B12 | Thaler99 | B13 |
| Gathergood19 | B16 | GalMcShane12 | B17 |
| Amar11 | B18 | Tsaban26 | B25 (preprint) |
| FHN | Part C §B | CFPB-FWB | Part C §A |
| JPMCI-buffer | Part C §E | Hyndman | Part C §G |

## Key takeaways for this app (engineering interpretation, not findings)

1. **Transaction data is research-grade** when transfers are excluded and income is identified
   deliberately — Gelman et al. (B1) did both, with keyword lists and by excluding inter-account transfers.
2. **Observed and perceived well-being differ** (A2, A3, B25). A transaction-based score should be called a
   *financial health* score of behaviour, not a well-being measure.
3. **Liquidity matters most** for distress (B10, B12, B2); weight it highest.
4. **Benchmarks are expert consensus** (B9), not outcome-validated thresholds — present them as guides.
5. **Debt repayment advice:** people follow neither avalanche nor snowball by default (B16); snowball has
   motivational support (B17) but costs money (B18). Default to avalanche, offer snowball, show the cost gap.
6. **Payday spikes are universal** (B1, B4) — forecasts should model pay cycles explicitly.
7. **No peer-reviewed household cash-flow forecasting paper on transaction data was found**; forecasting
   guidance relies on the general forecasting literature (Part C §G).

---

## Part A: Required papers

### A1. Developing a big data analytical model for measuring financial well-being in an open finance ecosystem
- **Authors:** Pilar Alvarez-Franco; Diego Alexander Restrepo-Tobón; Henry Laniado; Carolina Crispin-Fory; Henry Velasco
- **Year / venue:** 2026, *Journal of Economics, Finance and Administrative Science* 31(62), 228–248
- **DOI:** 10.1108/JEFAS-12-2024-0390, https://doi.org/10.1108/jefas-12-2024-0390
- **Open-access URL (from OpenAlex):** https://www.emerald.com/jefas/article-pdf/31/62/228/11436470/jefas-12-2024-0390en.pdf
- **Citations:** OpenAlex 1; S2 1 (checked 2026-11-24)
- **Methodology [abstract]:** A composite, customer-level financial well-being (FWB) index that combines objective behavioural indicators with subjective perceived-well-being measures. It was refined with feedback from about 100 industry experts and tested with sensitivity analyses. It was checked externally against nationally representative studies by CAF (Development Bank of Latin America and the Caribbean).
- **Methodology [full text, via WebFetch]:**
  - Sub-indicators are z-scored and weighted by PCA, following the JRC (2008) composite-indicator guidelines.
  - The composite weights objective components 70% and subjective components 30%.
  - The model is estimated separately for 5 age groups.
  - The full data did not meet PCA assumptions (KMO and Cronbach's alpha), so the authors used iterative subsampling.
  - Expert weights were used for validation only.
- **Dataset and sample [abstract]:** Proprietary demographic and transactional data for "nearly 430,000 clients" across several financial products, which approximates an open-finance setting.
- **Dataset and sample [full text, via WebFetch]:**
  - 432,695 clients and more than 50 million records, from September 2018 to August 2020.
  - The bank is described as "one of the largest banks in Latin America". The authors are affiliated with EAFIT (Colombia).
  - The subjective component comes from a survey subsample of 400 clients.
- **Objective indicator groups [full text, via WebFetch]:**
  - Sophistication: 7 indicators
  - Protection/insurance: 4
  - Wealth: 2, including debt/assets
  - Debt: 6, including debt service/income, card utilisation, cash advances and days past due
  - Savings: 3, including savings/income, emergency-fund coverage and regular saving
  - Planning: 3, namely income variability, expense variability and direct debits
  - Spending: 3, namely basic expenses/income, non-basic expenses/income and paycheck-to-paycheck
- **Key findings:**
  - [abstract] The model captures the proposed conceptual model across life stages. Its data-driven weights reflect lifecycle consumption theory. It is suitable to scale in open finance.
  - [full text, via WebFetch] The PCA index and the expert-weighted index correlate at about 0.70.
  - [full text, via WebFetch] Distributions stay stable across subsamples: in more than 95% of 499,500 Mann-Whitney pairs, the hypothesis of equal distributions was not rejected.
- **Formulas:** The source gives no closed-form equation (WebFetch). The method is z-scores, then PCA loadings, then a 70/30 objective/subjective weighting.
- **Relevance to a Plaid-based app:** This is the closest published analogue to a transaction-derived health score. Its objective indicators can mostly be computed from ledger data: income and expense variability, savings/income, basic vs non-basic spend, card utilisation, emergency-fund months and direct debits. The 70/30 objective/subjective weighting is a design choice worth arguing about.
- **Limitations:**
  - One proprietary institution, and only an approximation of open finance [abstract and full text].
  - The authors say weighting is a judgment call (full text, via WebFetch).
  - The CAF validation and the limitations section (5.3) were in the unread part of the page.
- **Verified via:** OpenAlex, S2, publisher page (WebFetch). **Flags:** the full-text details come through a summarising model and only part of the page.

### A2. Measuring Financial Wellbeing with Self-Reported and Bank-Record Data
- **Title match:** The title matches, apart from the hyphen in "Bank-Record". The brief guessed Muir et al., which is **wrong**. The authors are Carole Comerton-Forde, John de New (OpenAlex: John P. Haisken-DeNew), Nicolás Salamanca, David C. Ribar, Andrea Nicastro and James Ross.
- **Year / venue:** *Economic Record* 98(321), 133–151, print issue June 2022 (Crossref). OpenAlex gives 2020, which is the online-first year.
- **DOI:** 10.1111/1475-4932.12664, https://doi.org/10.1111/1475-4932.12664. OpenAlex lists the DOI link as the open-access location.
- **Citations:** OpenAlex 70; S2 29; Crossref 36 (checked 2026-11-24)
- **Methodology [abstract]:** Item response theory (IRT) models build two scales:
  1. A Reported Financial Wellbeing Scale, from survey experiences and perceptions.
  2. An Observed Financial Wellbeing Scale, from bank-record measures: account balances, net spending and payment problems.

  Validity is checked with predictive models.
- **Dataset and sample [abstract]:** Customers of a major Australian bank, with survey answers matched to their financial records. **Sample size:** not verified, because the Wiley full text returned 403.
- **Key findings [abstract]:** Each scale reliably separates a wide range of outcomes, and the scale components have similar power to discriminate.
- **Formulas:** IRT is stated. The exact model (for example graded response) is not verified.
- **Relevance:** This is direct evidence that balances, net spending and payment problems (overdrafts, late or failed payments) carry well-being information comparable to survey data. Those three inputs are what a Plaid ledger can supply. It supports building an "observed" score and keeping it separate from any self-report.
- **Limitations:** One bank, so customers' accounts at other banks are not seen. That is exactly the multi-institution gap a Plaid aggregator closes. The authors' own stated limitations are not verified.
- **Verified via:** OpenAlex, Crossref, S2.

### A3. Perceptions or behavior? An evaluation of CFPB's financial well-being scale using household financial ratios
- **Authors:** Edmund Khashadourian; Allen F. Harrison
- **Year / venue:** 2024, *Financial Planning Review* 7(3–4), e1194
- **DOI:** 10.1002/cfp2.1194, https://doi.org/10.1002/cfp2.1194
- **Open-access URL:** https://onlinelibrary.wiley.com/doi/pdfdirect/10.1002/cfp2.1194 (could not be fetched here: HTTP 403)
- **Citations:** OpenAlex 8; S2 4 (checked 2026-11-24)
- **Methodology [abstract]:** A conceptual "equilibrium model of the household" (EMH). Discriminant analysis on combinations of household financial ratios extracts four financial well-being categories: distressed, fragile, stable and flourishing. These categories are compared with CFPB Financial Well-Being Scale scores.
- **Dataset and sample:** not verified. The CFPB data is implied but not named in the abstract.
- **Key findings [abstract]:**
  - Higher CFPB scores go with higher category ranks, which supports the scale's construct validity and lets CFPB score ranges map to concrete financial outcomes.
  - However, the authors argue that the claim that the CFPB scale measures something beyond traditional financial measures is imprecise, and may reflect noise in CFPB's data.
- **Formulas:** The specific ratios are not verified.
- **Relevance:** This supports a tiered health label (distressed / fragile / stable / flourishing) driven by ratios rather than a survey. That suits an app that has transactions and balances but no questionnaire.
- **Limitations [abstract]:** The authors question the CFPB scale's reliability. Their own limitations are not verified.
- **Verified via:** OpenAlex, S2.

### A4. Financial planning behaviour: a systematic literature review and new theory development
- **Authors:** Kingsley Hung Khai Yeo; Weng Marc Lim; Kwang-Jing Yii
- **Year / venue:** OpenAlex and S2 give 2023 (online). Print is *Journal of Financial Services Marketing* 29(3), 979–1001; the print year is not verified, probably 2024.
- **DOI:** 10.1057/s41264-023-00249-1. Open access: https://link.springer.com/content/pdf/10.1057/s41264-023-00249-1.pdf
- **Citations:** OpenAlex 262; S2 146 (checked 2026-11-24)
- **Methodology [abstract]:** A systematic review using the SPAR-4-SLR protocol, through the lens of the Theory of Planned Behaviour. It analyses the literature with the ADO (antecedents, decisions, outcomes) and TCM (theories, contexts, methods) frameworks.
- **Dataset and sample [abstract]:** 30 articles indexed in Scopus and Web of Science.
- **Key findings [abstract]:** It proposes a "theory of financial planning behaviour":
  - Financial satisfaction (attitude) and financial socialisation (subjective norms) drive the intention to plan.
  - Financial literacy, mental accounting and financial cognition (perceived behavioural control) also drive that intention.
  - Intention then shapes adoption of 6 forms of planning: cash flow, tax, investment, risk, estate and retirement.
- **Relevance:** A theoretical frame for the research document. It places cash-flow planning, which is what a budgeting app supports, as one of six planning domains, and names mental accounting as a lever.
- **Limitations:** Only 30 articles. The theory is conceptual and untested (inferred from the abstract).
- **Verified via:** OpenAlex, S2.

### A5. Household Finance
- **Authors:** Francisco Gomes; Michael Haliassos; Tarun Ramadorai
- **Year / venue:** 2021, *Journal of Economic Literature* 59(3), 919–1000
- **DOI:** 10.1257/jel.20201461, https://doi.org/10.1257/jel.20201461. OpenAlex lists no open-access URL.
- **Citations:** OpenAlex 625; S2 88 (S2 looks under-counted) (checked 2026-11-24)
- **Methodology [abstract]:** A literature survey.
- **Coverage [abstract]:** Asset participation and allocation over the life cycle; insurance; trading; retirement saving; mortgages and refinancing/default; unsecured credit (credit cards, payday lending); peer and cultural effects; financial literacy; and financial advice.
- **Dataset:** Not applicable.
- **Relevance:** The standard reference for framing household balance-sheet and credit-card behaviour.
- **Verified via:** OpenAlex, S2.

### A6. Financial literacy around the world: an overview
- **Authors:** Annamaria Lusardi; Olivia S. Mitchell
- **Year / venue:** 2011, *Journal of Pension Economics and Finance* 10(4), 497–508
- **DOI:** 10.1017/S1474747211000448, https://doi.org/10.1017/s1474747211000448
- **Open-access URL:** https://repository.upenn.edu/bepp_papers/146. An NBER version is w17107.
- **DOI trap:** The neighbouring DOI 10.1017/S147474721100045X is a *different* paper in the same issue: "Financial Literacy and Retirement Planning in the United States". Do not mix them up.
- **Citations:** OpenAlex 3,171; S2 1,712 (checked 2026-11-24)
- **Methodology [abstract]:** A cross-country comparison of the "Big Three" style literacy questions. Instrumental variables are used for the effect of literacy on planning. The name "Big Three" is not in the abstract and comes from wider literature.
- **Dataset [abstract]:** Surveys in Germany, the Netherlands, Sweden, Japan, Italy, New Zealand, the US and Russia. Sample sizes are not verified.
- **Key findings [abstract]:**
  - Financial illiteracy is widespread.
  - Older people overrate their knowledge.
  - There are gender gaps.
  - One more correct answer goes with a 3–4 percentage point higher chance of retirement planning (10 percentage points in the Netherlands).
  - The IV estimates suggest OLS understates the effect.
- **Relevance:** Justifies explanatory, plain-language guidance in the app, rather than assuming users understand interest or inflation.
- **Verified via:** OpenAlex, S2.

---

## Part B: Additional papers

### Real bank or aggregator transaction data

#### B1. Harnessing naturally occurring data to measure the response of spending to income
- **Authors:** Michael Gelman; Shachar Kariv; Matthew D. Shapiro; Dan Silverman; Steven Tadelis
- **Year / venue:** 2014, *Science* 345(6193), 212–215
- **DOI:** 10.1126/science.1247727. Open access on PMC: https://www.ncbi.nlm.nih.gov/pmc/articles/4850072
- **Citations:** OpenAlex 221; S2 169 (checked 2026-11-24)
- **Methodology:**
  - [abstract] A data infrastructure built from an account aggregator's transactions and balances, used to test whether spending responds to the arrival of anticipated income.
  - [full text] Income is measured two ways: (1) all credits to non-credit-card accounts, excluding transfers between accounts; and (2) paychecks and Social Security, identified by keyword lists on the transaction description. The authors also separate recurring from non-recurring income and spending.
- **Dataset [full text]:**
  - The Check personal-finance app, which had about 1.5 million active US users in 2012.
  - A random sample of about **75,000** users with at least one bank or card account.
  - 300 consecutive days across 2012–2013.
  - The sample over-represents males and younger adults [abstract].
- **Key findings:**
  - [full text] Spending rises above average on the day a paycheck or Social Security payment arrives, and stays high for at least 4 days.
  - [abstract] Most of this excess sensitivity comes from regular income and regular spending (for example bills) arriving at the same time. What remains is concentrated among people with less liquidity.
- **Relevance:** The closest methodological template for a Plaid ledger. It covers excluding internal transfers, keyword-based income detection and recurring-flow detection. It also warns that payday spending spikes are partly just scheduled bills, which matters for forecasting and recurring detection.
- **Limitations:** The sample is not representative [abstract].
- **Verified via:** OpenAlex, S2, PMC full text.

#### B2. Debt and the Response to Household Income Shocks: Validation and Application of Linked Financial Account Data
- **Author:** Scott R. Baker
- **Year / venue:** 2018, *Journal of Political Economy* 126(4), 1504–1557
- **DOI:** 10.1086/698106. No open-access URL in OpenAlex.
- **Citations:** OpenAlex 348; S2 262 (checked 2026-11-24)
- **Methodology [abstract]:** Linked-account transaction and balance data are validated by matching their distributional moments to external sources. As an application, consumption elasticity to income shocks is estimated across households with different levels and types of debt.
- **Dataset [abstract]:** "Millions of Americans", from what Baker calls the most comprehensive existing linked-account dataset. The provider and exact N are not verified.
- **Key findings [abstract]:** Heterogeneity in consumption elasticity "can be explained entirely by credit and liquidity".
- **Relevance:** A validation template showing how to check whether linked-account data represents the population. It also supports liquidity and credit headroom as core health-score inputs.
- **Limitations:** The abstract discusses data "concerns". The detail is not verified.
- **Verified via:** OpenAlex, S2.

#### B3. Consumer Spending during Unemployment: Positive and Normative Implications
- **Authors:** Peter Ganong; Pascal Noel
- **Year / venue:** 2019, *American Economic Review* 109(7), 2383–2424
- **DOI:** 10.1257/aer.20170537. Open access: https://www.aeaweb.org/articles/pdf/doi/10.1257/aer.20170537
- **Citations:** OpenAlex 557; S2 355 (checked 2026-11-24)
- **Methodology [abstract]:** De-identified bank-account data, with a high-frequency event study around the predictable exhaustion of unemployment insurance (UI).
- **Dataset:** The bank and N are not verified. The abstract says only "de-identified bank account data".
- **Key findings [abstract]:**
  - Spending drops sharply when UI is exhausted.
  - This fits behavioural models (present-biased or myopic households), not rational models of liquidity-constrained households.
  - Extending UI duration is worth 4 times as much for consumption smoothing as raising benefit levels.
- **Relevance:** Predictable income cliffs are visible in transaction data, and users do not smooth across them. That justifies forward-looking cash-flow warnings.
- **Verified via:** OpenAlex, S2.

#### B4. The Liquid Hand-to-Mouth: Evidence from Personal Finance Management Software
- **Authors:** Arna Olafsson; Michaela Pagel
- **Year / venue:** 2018, *Review of Financial Studies* 31(11), 4398–4446
- **DOI:** 10.1093/rfs/hhy055. Repository: https://research.cbs.dk/en/publications/f06270e6-0f94-41ff-85d8-5f10c935d105
- **Citations:** OpenAlex 263; S2 196 (checked 2026-11-24)
- **Methodology [abstract]:** A panel from personal-finance software covering all individual spending, income, balances and credit limits. It measures payday responses to regular and irregular income.
- **Dataset:** The country, provider and N are not verified. They are not in the abstract.
- **Key findings [abstract]:**
  - Payday spending responses are robust and similar across all income and spending categories and across the income distribution.
  - Few people hold little or no liquidity.
  - Responses fit standard models without illiquid savings, in which liquidity constraints rarely bind.
- **Relevance:** Personal-finance-app data, like this app's, is usable for research-grade measurement. Payday spikes are universal, so a forecast should model pay cycles.
- **Verified via:** OpenAlex, S2.

#### B5. Paychecks, Paydays, and the Online Platform Economy: Big Data on Income Volatility
- **Authors:** Diana Farrell; Fiona Greig (JPMorgan Chase Institute)
- **Year / venue:** OpenAlex: 2016, SSRN, no DOI. It is an institute report, **not peer-reviewed**.
- **URL (OpenAlex):** https://papers.ssrn.com/sol3/Delivery.cfm/SSRN_ID2911293_code2375583.pdf?abstractid=2911293&mirid=1&type=2
- **Citations:** OpenAlex 251; S2 not checked (checked 2026-11-24)
- **Methodology, dataset, findings:** not verified, because OpenAlex has no abstract and the report was not fetched.
- **Relevance:** A standard reference for month-to-month income volatility measured from bank accounts.
- **Flags:** Grey literature. All content details are not verified. The related JPMC report "Weathering Volatility" (2015) did not resolve in OpenAlex.
- **Verified via:** OpenAlex (metadata only).

#### B6. Income Gains and Month-to-Month Income Volatility: Household Evidence from the US Financial Diaries
- **Authors:** Anthony Hannagan; Jonathan Morduch
- **Year / venue:** 2015, SSRN working paper (US Financial Diaries / NYU Wagner). **Not peer-reviewed.**
- **DOI:** 10.2139/ssrn.2659883
- **Citations:** OpenAlex 76; S2 70 (checked 2026-11-24)
- **Methodology, dataset, findings:** not verified, because there is no abstract in OpenAlex.
- **Relevance:** Income volatility at monthly frequency. It supports using a volatility metric, such as the coefficient of variation of monthly income, in a health score. That metric is a suggestion, not something the paper is verified to use.
- **Verified via:** OpenAlex, S2 (metadata only).

### Financial well-being: concept and measurement

#### B7. How Am I Doing? Perceived Financial Well-Being, Its Potential Antecedents, and Its Relation to Overall Well-Being
- **Authors:** Richard G. Netemeyer; Dee Warmath; Daniel Fernandes; John G. Lynch Jr.
- **Year / venue:** *Journal of Consumer Research* 45(1), 68–89. Print year 2018 per S2; OpenAlex gives 2017, the online year.
- **DOI:** 10.1093/jcr/ucx109. Open access via SSRN: https://doi.org/10.2139/ssrn.3485990
- **Citations:** OpenAlex 1,272; S2 796 (checked 2026-11-24)
- **Methodology [abstract]:** Consumer financial narratives, several large-scale surveys and 2 experiments are used to develop and validate scales.
- **Dataset:** Sample sizes are not verified.
- **Key findings [abstract]:**
  - Perceived financial well-being has 2 constructs: current money management stress and expected future financial security.
  - Perceived financial well-being predicts overall well-being about as strongly as job satisfaction, physical health and relationship support combined.
  - The relative importance of money management stress differs by income group.
- **Relevance:** Argues for splitting a health score into a "today" dimension (cash flow, bills) and a "future" dimension (savings, debt trajectory).
- **Verified via:** OpenAlex, S2.

#### B8. Financial well-being: A conceptualization and research agenda
- **Authors:** Elisabeth C. Brüggen; Jens Hogreve; Maria Holmlund; Sertan Kabadayi; Martin Löfgren
- **Year / venue:** 2017, *Journal of Business Research* 79, 228–237
- **DOI:** 10.1016/j.jbusres.2017.03.013. No open-access URL in OpenAlex.
- **Citations:** OpenAlex 1,520; S2 932; Crossref 797 (checked 2026-11-24)
- **Methodology, findings:** **not verified.** Neither OpenAlex nor Crossref returned an abstract. It is a conceptual paper, as the title implies, and is often cited for a definition of financial well-being; that definition was not verified here.
- **Verified via:** OpenAlex, Crossref, S2 (metadata only).

### Household financial ratios

#### B9. Ratios and Benchmarks for Measuring the Financial Well-Being of Families and Individuals
- **Authors:** Sue Alexander Greninger; Vickie L. Hampton; Karrol A. Kitt; Joseph A. Achacoso (OpenAlex spells the third name "Kamd A. Kitt", which looks like an OCR error)
- **Year / venue:** 1996, *Financial Services Review* 5(1), 57–70
- **DOI:** 10.1016/S1057-0810(96)90027-X
- **Citations:** OpenAlex 198; S2 153 (checked 2026-11-24)
- **Methodology [abstract]:** A Delphi study with a panel of **156** experts (financial planners and educators).
- **Key findings [abstract]:**
  - The two groups agreed on benchmarks for 20 of 22 ratios. The ratios cover liquidity, savings, asset allocation, inflation protection, tax burden, housing expenses and insolvency/credit.
  - There was consensus on the usefulness of liquidity and tax-burden ratios, but not of inflation-protection or insolvency/credit ratios.
  - Simpler ratios were preferred.
  - The authors propose a profile of a typical family's financial well-being.
- **Benchmarks [full text, Table 1 and Section IV, verified 2026-11-24]:** panel medians (N = 156):
  - Liquid assets ÷ monthly expenses: **300 %** (summary table: ≥ 250 %). Liquid assets = cash, checking, savings, money market accounts and funds, CDs ≤ 6 months. Monthly expenses = average fixed and variable living expenses *including debt repayment, taxes and set-asides for irregular expenses*.
  - Savings ÷ gross income: **10 %**; savings ÷ net income: **10 %**.
  - Renter's housing ÷ gross income: **30 %**; homeowner's: **35 %**.
  - Non-mortgage debt payments ÷ after-tax income: **10 %** reasonable (summary table ≤ 15 %), **20 %** danger point.
  - Total debt payments ÷ after-tax income: **35 %** reasonable, **45 %** danger point.
  - Total expenses ÷ after-tax income: **80 %** reasonable, **90 %** danger point.
  - Experts preferred liquid assets ÷ monthly expenses over liquid assets ÷ current debt.
- **Relevance:** The canonical source for ratio thresholds in a health score: liquidity, savings rate, housing cost/income and debt/income.
- **Verified via:** OpenAlex, S2.

#### B10. The Usefulness of Financial Ratios as Predictors of Household Insolvency: Two Perspectives
- **Author:** Sharon A. DeVaney
- **Year / venue:** 1994. OpenAlex lists it as SSRN with no DOI. The journal of record is believed to be *Financial Counseling and Planning*, vol. 5, but that is **not verified**.
- **URL (OpenAlex):** http://6aa7f5c4a9901a3e1a1682793cd11f5a6b732d29.gripelements.com/pdf/vol-51.pdf
- **Citations:** OpenAlex 92; S2 not checked (checked 2026-11-24)
- **Methodology [abstract]:** Financial ratios for households, analysed with logistic regression and a classification tree (CART).
- **Dataset [abstract]:** Survey of Consumer Finances. **1,934** households, with 1983 ratios predicting 1986 insolvency.
- **Key findings [abstract]:**
  - The liquidity ratio was the strongest predictor in the logistic regression.
  - Assets/liabilities was the strongest in CART.
  - Gross annual debt payments to disposable income was second in both.
- **Relevance:** Evidence that liquidity and debt-service ratios predict distress, so they justify heavy weights in a health score.
- **Limitations:** Old survey data (1983–86). Insolvency is defined by assets and liabilities, not by cash flow.
- **Verified via:** OpenAlex. Venue not verified.

#### B11. How to Use Financial Ratios When Advising Clients
- **Authors:** Ruth H. Lytton; E. Thomas Garman; Nancy M. Porter
- **Year / venue:** 1991. OpenAlex lists SSRN with no DOI. The journal of record is not verified, possibly *Financial Counseling and Planning* vol. 2.
- **Citations:** OpenAlex 57 (checked 2026-11-24)
- **Content:** not verified, because there is no abstract.
- **Verified via:** OpenAlex (metadata only).

#### B12. Financially Fragile Households: Evidence and Implications
- **Authors:** Annamaria Lusardi; Daniel Schneider; Peter Tufano
- **Year / venue:** 2011, *Brookings Papers on Economic Activity* 2011(1), 83–134
- **DOI:** 10.1353/eca.2011.0002. Open access: https://muse.jhu.edu/pub/11/article/449772/pdf
- **Citations:** OpenAlex 383; S2 n/a (checked 2026-11-24)
- **Methodology [abstract]:** Survey measure of whether a household could come up with $2,000 within 30 days.
- **Dataset [abstract]:** 2009 TNS Global Economic Crisis Study, covering the US and 7 other industrialised countries. N is not verified.
- **Key findings [abstract]:**
  - About 25% of US respondents are certain they could not raise the sum. Close to 50% are fragile if "probably not" answers are included.
  - Fragility reaches into the middle class.
  - Coping methods follow a "pecking order" with own savings first, then family and friends, credit, extra work and selling possessions.
- **Relevance:** Provides the "can you absorb a shock" framing. An app can approximate it as liquid balances divided by typical monthly spending (emergency-fund months). That approximation is an inference, not the paper's method.
- **Verified via:** OpenAlex.

### Behavioural budgeting, saving and debt repayment

#### B13. Mental accounting matters
- **Author:** Richard H. Thaler
- **Year / venue:** 1999, *Journal of Behavioral Decision Making* 12(3), 183–206
- **DOI:** 10.1002/(SICI)1099-0771(199909)12:3<183::AID-BDM318>3.0.CO;2-F
- **Citations:** OpenAlex 4,536; S2 not checked (checked 2026-11-24)
- **Content [abstract]:** A review paper. Mental accounting has 3 components:
  1. How outcomes are perceived and evaluated.
  2. Assigning activities to accounts: spending is grouped into categories and constrained by budgets.
  3. How often accounts are evaluated, and choice bracketing.

  All three violate fungibility.
- **Relevance:** The behavioural basis for category budgets and for weekly vs monthly views.
- **Verified via:** OpenAlex.

#### B14. Golden Eggs and Hyperbolic Discounting
- **Author:** David Laibson
- **Year / venue:** 1997, *Quarterly Journal of Economics* 112(2), 443–478
- **DOI:** 10.1162/003355397555253. Open access: https://dash.harvard.edu/bitstream/1/4481499/2/Laibson_GoldenEggs.pdf
- **Citations:** OpenAlex 7,622 (checked 2026-11-24)
- **Content [abstract]:** A theoretical model of a hyperbolic (present-biased) consumer with an illiquid asset used as commitment. It predicts that consumption tracks income. Financial innovation that adds liquidity may lower saving and welfare.
- **Formulas:** Hyperbolic discounting is stated. The quasi-hyperbolic β-δ specification is widely associated with this paper, but is not verified from the abstract.
- **Relevance:** Theory behind commitment and "locked" savings goals.
- **Verified via:** OpenAlex.

#### B15. Save More Tomorrow: Using Behavioral Economics to Increase Employee Saving
- **Authors:** Richard H. Thaler; Shlomo Benartzi
- **Year / venue:** 2004, *Journal of Political Economy* 112(S1), S164–S187
- **DOI:** 10.1086/380085
- **Citations:** OpenAlex 3,105; S2 2,707 (checked 2026-11-24)
- **Methodology [abstract]:** Field implementations of a prescriptive programme in which employees commit in advance to save part of future pay rises.
- **Findings [abstract], first implementation:**
  - 78% of those offered the plan joined.
  - 80% of those enrolled stayed through 4 pay raises.
  - Saving rates rose from 3.5% to 13.6% over 40 months.
- **Dataset:** Employer sites. The N per site is not verified.
- **Relevance:** A design pattern for an app: suggest raising savings when the app detects a pay rise in the income stream.
- **Verified via:** OpenAlex, S2.

#### B16. How Do Individuals Repay Their Debt? The Balance-Matching Heuristic
- **Authors:** John Gathergood; Neale Mahoney; Neil Stewart; Jörg Weber
- **Year / venue:** 2019, *American Economic Review* 109(3), 844–875
- **DOI:** **10.1257/aer.20180288.** The DOI suggested in the brief, 10.1257/aer.20160977, returned 404 in OpenAlex. Use the one here. Open access: https://www.aeaweb.org/articles/pdf/doi/10.1257/aer.20180288
- **Citations:** OpenAlex 200; S2 112 (checked 2026-11-24)
- **Methodology [abstract]:** Linked data on individuals' multiple credit cards.
- **Dataset:** The country and N are not verified. They are not in the abstract.
- **Key findings [abstract]:**
  - People do not direct repayments to the highest-interest card.
  - The misallocation does not change with the stakes.
  - Repayments follow **balance matching**: each card's share of repayments matches its share of total balances. This explains more than half of the predictable variation and persists over time.
- **Formula [abstract, stated in words]:** repayment_share(card i) ≈ balance_i / Σ balances.
- **Relevance:** Real-world evidence that users do neither avalanche nor snowball by default. An avalanche (highest APR first) recommendation has a measurable cost-saving case.
- **Verified via:** OpenAlex, S2.

#### B17. Can Small Victories Help Win the War? Evidence from Consumer Debt Management
- **Authors:** David Gal; Blakeley B. McShane
- **Year / venue:** 2012, *Journal of Marketing Research* 49(4), 487–501
- **DOI:** **10.1509/jmr.11.0272.** The DOI suggested in the brief, 10.1509/jmr.11.0118, returned 404.
- **Citations:** OpenAlex 95; S2 85 (checked 2026-11-24)
- **Methodology [abstract]:** Observational analysis of a dataset from a debt settlement firm.
- **Dataset:** N is not verified.
- **Key findings [abstract]:**
  - Closing debt accounts predicts eliminating all debt, regardless of the dollar balance of the accounts closed.
  - Once the fraction of accounts closed is controlled for, the dollar balance closed does not predict debt elimination.
  - Completing discrete subtasks appears to motivate persistence.
- **Relevance:** The empirical case for the snowball method (smallest balance first) on motivational grounds.
- **Limitations:** Observational, and the population is debt-settlement clients.
- **Verified via:** OpenAlex, S2.

#### B18. Winning the Battle but Losing the War: The Psychology of Debt Management
- **Authors:** Moty Amar; Dan Ariely; Shahar Ayal; Cynthia E. Cryder; Scott I. Rick
- **Year / venue:** 2011, *Journal of Marketing Research* 48(SPL), S38–S50
- **DOI:** 10.1509/jmkr.48.SPL.S38
- **Citations:** OpenAlex 176; S2 161 (checked 2026-11-24)
- **Methodology [abstract]:** 4 experiments with an incentive-compatible debt management game.
- **Dataset:** Participant N is not verified.
- **Key findings [abstract]:**
  - "Debt account aversion": participants pay off small debts first even when larger debts carry higher interest.
  - Blocking full payoff of small debts, and focusing attention on accumulated interest, reduces total debt faster.
- **Relevance:** The counterweight to B17. Snowball costs money, and showing accumulated interest helps people choose avalanche. Together, B16–B18 support offering avalanche as the default, with snowball as an option and the interest difference shown.
- **Verified via:** OpenAlex, S2.

### Transaction categorisation and ML on transaction data

#### B19. Making Use of External Company Data to Improve the Classification of Bank Transactions
- **Authors:** Erlend Vollset; Eirik Folkestad; Marius Rise Gallala; Jon Atle Gulla
- **Year / venue:** 2017, *Advanced Data Mining and Applications (ADMA 2017)*, Lecture Notes in Computer Science, pp. 767–780 (peer-reviewed conference)
- **DOI:** 10.1007/978-3-319-69179-4_54
- **Citations:** OpenAlex 8; S2 6 (checked 2026-11-24)
- **Content:** Methodology, dataset and findings are **not verified**, because there is no abstract. A related NTNU master's thesis, "Automatic Classification of Bank Transactions", is at https://hdl.handle.net/11250/2456871.
- **Verified via:** OpenAlex, S2 (metadata only).

#### B20. Explainable Automatic Industrial Carbon Footprint Estimation From Bank Transaction Classification Using Natural Language Processing
- **Authors:** Jaime González-González; Silvia García-Méndez; Francisco de Arriba-Pérez; Francisco J. González-Castaño; Óscar Barba-Seara
- **Year / venue:** 2022, *IEEE Access* 10, 126326–126338. S2 gives 2024.
- **DOI:** 10.1109/ACCESS.2022.3226324. Open access: https://ieeexplore.ieee.org/ielx7/6287639/6514899/09968254.pdf
- **Citations:** OpenAlex 27; S2 15 (checked 2026-11-24)
- **Methodology [abstract]:**
  - Classifies bank transactions from their description text using SVM, Random Forest and recurrent neural networks. The abstract writes "Recursive Neural Networks".
  - Explains predictions with LIME-style local interpretable models. Explanation terms are validated by similarity to the category descriptions.
- **Dataset:** Not verified.
- **Key findings [abstract]:** Accuracy, precision and recall are "in the 90% range".
- **Relevance:** A peer-reviewed example of classifying transaction descriptions, with explainability. That is relevant to showing users why a row got its category. The end use here is carbon accounting, not budgeting.
- **Verified via:** OpenAlex, S2.

#### B21. Scalable and Weakly Supervised Bank Transaction Classification
- **Authors:** Liam Toran; Cory Van Der Walt; Alan Sammarone; Alex Keller
- **Year / venue:** 2023, arXiv 2305.18430. A **preprint, not peer-reviewed**.
- **DOI:** 10.48550/arXiv.2305.18430. https://arxiv.org/abs/2305.18430
- **Citations:** OpenAlex 7; S2 3 (checked 2026-11-24)
- **Methodology [abstract]:** Weak supervision from heuristics and domain knowledge, plus NLP text embeddings and a discriminative neural network. The pipeline covers preprocessing, embedding, anchoring and label generation.
- **Key findings [abstract]:** The authors say it outperforms market-leading solutions. The numbers are not verified.
- **Relevance:** Matches this app's design of rules first, then a model. Keyword rules act as weak labels.
- **Verified via:** OpenAlex, S2.

#### B22. Retail Credit Scoring using Fine-Grained Payment Data
- **Authors:** Ellen Tobback; David Martens
- **Year / venue:** 2019, *Journal of the Royal Statistical Society Series A* 182(4), 1227–1246
- **DOI:** 10.1111/rssa.12469. Open access: https://repository.uantwerpen.be/docman/irua/9e8410/146341.pdf
- **Citations:** OpenAlex 30; S2 24 (checked 2026-11-24)
- **Methodology [abstract]:** Scalable predictive credit-scoring models built on fine-grained payment (transaction) data.
- **Dataset [abstract]:** **183 million transactions from 2.6 million customers** of a bank.
- **Key findings [abstract]:**
  - AUC improves significantly, with only seconds of computation.
  - Among the riskiest 1% of customers, the model finds twice as many defaulters when payment data is used.
- **Relevance:** Shows that transaction-level data predicts financial distress, which is the predictive validity behind a transaction-based health score.
- **Verified via:** OpenAlex, S2.

#### B23. Consumer credit-risk models via machine-learning algorithms
- **Authors:** Amir E. Khandani; Adlar J. Kim; Andrew W. Lo
- **Year / venue:** 2010, *Journal of Banking & Finance* 34(11), 2767–2787
- **DOI:** 10.1016/j.jbankfin.2010.06.001. SSRN version: https://doi.org/10.2139/ssrn.1568864
- **Citations:** OpenAlex 1,419 (checked 2026-11-24)
- **Content:** Methodology and dataset are **not verified**, because OpenAlex has no abstract. It is often described as combining bank transaction data with credit-bureau data to forecast delinquency; check that against the paper before citing it that way.
- **Verified via:** OpenAlex (metadata only).

### Open banking (2022–2026)

#### B24. Customer data access and fintech entry: Early evidence from open banking
- **Authors:** Tania Babina; Saleem Bahaj; Greg Buchak; Filippo De Marco; Angus Foulis; Will Gornall; Francesco Mazzola; Tong Yu
- **Year / venue:** 2025, *Journal of Financial Economics* 169, 103950 (NBER w32089, 2024)
- **DOI:** 10.1016/j.jfineco.2024.103950. Open access per OpenAlex.
- **Citations:** OpenAlex 139; S2 n/a (checked 2026-11-24)
- **Methodology [abstract]:** Cross-country policy data, UK microdata and a calibrated model.
- **Findings [abstract]:**
  - 49 countries have adopted open-banking (OB) policies, and privacy preferences predict adoption.
  - Adoption spurs fintech entry.
  - In UK microdata, OB lets consumers access advice and credit.
  - In the model, OB used for advice improves welfare for everyone. OB used for credit helps competition, but costlier or privacy-conscious consumers pay higher prices.
- **Relevance:** Policy context for an aggregator app. It also supports the app's privacy-first stance: privacy preferences matter, and credit uses carry distributional costs.
- **Verified via:** OpenAlex.

#### B25 (optional, flagged). Same Finances, Different Scores: Unexplained Dispersion in the CFPB Financial Well-Being Scale
- **Author:** Shay Tsaban
- **Year / venue:** 2026, Research Square **preprint, not peer-reviewed**
- **DOI:** 10.21203/rs.3.rs-10542396/v1
- **Citations:** OpenAlex 0 (checked 2026-11-24)
- **Data [abstract]:** CFPB National Financial Well-Being Survey (n = 6,389) and the Making Ends Meet survey (January 2025, n = 2,623).
- **Findings [abstract]:**
  - 32 indicators of financial position, plus demographics, explain just over half the variance in the CFPB score.
  - Households in the same decile of predicted financial position differ by 17–28 points on the 100-point scale.
  - The unexplained part tracks temperament and life satisfaction.
- **Relevance:** Supports A3's argument that objective and subjective scores diverge. A transaction-based score should not claim to be the same thing as the CFPB scale.
- **Verified via:** OpenAlex. Include it only if preprints are acceptable.

---

## Gaps and notes

- **No peer-reviewed household cash-flow or expense forecasting paper on transaction data was found.** OpenAlex searches returned only low-quality engineering-journal app papers (for example IJRASET 2025–26), which are not recommended. Gelman et al. (B1) and Olafsson & Pagel (B4) give the empirical basis for payday and recurring structure. The general forecasting review "Forecasting: theory and practice" (Petropoulos et al., *International Journal of Forecasting* 2022, DOI 10.1016/j.ijforecast.2021.11.001, OpenAlex 1,496 citations) can cover methods; its content was not reviewed here.
- **The CFPB Financial Well-Being Scale technical report (2017)** is a government report and was not found in OpenAlex. Cite it from consumerfinance.gov directly. It is not verified here.
- **No JPMorgan Chase Institute report has a DOI.** B5 is metadata-only.
- Several high-value details are **not verified** because only abstracts were available: sample sizes for B2, B3, B4, B16, B17 and B18, the ratio formulas in A3 and B9, and everything in B8, B11, B19 and B23. Pull the full texts before quoting those.

---

## Part C: Institutional sources (CFPB, FHN, Federal Reserve, JPMCI, Plaid, forecasting, regulation)

Researched 2026-11-24. Every number below was read from the source URL given unless it is marked **not verified**. Quotes are verbatim from the source text (PDFs were text-extracted locally). The "Status" line on each topic says what is confirmed.

---

### A. CFPB Financial Well-Being Scale

**Status:** item wording, response scales, scoring tables, IRT basis, 0–100 range, 2017 mean and percentiles all verified from CFPB PDFs. The scale-development technical report itself was not opened (its URL is cited inside the 2017 report). The standard deviation was not extracted.

#### Sources
- Landing page: https://www.consumerfinance.gov/data-research/research-reports/financial-well-being-scale/
- User guide (Dec 2015), *Measuring financial well-being: A guide to using the CFPB Financial Well-Being Scale*: https://files.consumerfinance.gov/f/201512_cfpb_financial-well-being-user-guide-scale.pdf
- 10-item questionnaire and scoring worksheet: https://files.consumerfinance.gov/f/documents/bcfp_fin-well-being_full-scorecard.pdf
- 5-item questionnaire and scoring worksheet: https://files.consumerfinance.gov/f/documents/bcfp_fin-well-being_short-scorecard.pdf
- *Financial well-being in America* (Sept 2017): https://files.consumerfinance.gov/f/documents/201709_cfpb_financial-well-being-in-America.pdf
- Technical report (cited in the 2017 report, footnote 24; not opened): https://www.consumerfinance.gov/data-research/research-reports/financial-well-being-technical-report/
- 2017→2020 follow-up data spotlight (seen in search results only; "54 to 55" **not verified** on the page): https://www.consumerfinance.gov/consumer-tools/educator-tools/financial-well-being-resources/data-spotlight-financial-well-being-in-america-2017-2020/

#### 10-item version (standard), exact wording
Part 1, "How well does this statement describe you or your situation?" Response options: Completely / Very well / Somewhat / Very little / Not at all.

| # | Item | Points (Completely→Not at all) |
|---|------|------|
| 1 | I could handle a major unexpected expense | 4 3 2 1 0 |
| 2 | I am securing my financial future | 4 3 2 1 0 |
| 3 | Because of my money situation, I feel like I will never have the things I want in life | 0 1 2 3 4 |
| 4 | I can enjoy life because of the way I'm managing my money | 4 3 2 1 0 |
| 5 | I am just getting by financially | 0 1 2 3 4 |
| 6 | I am concerned that the money I have or will save won't last | 0 1 2 3 4 |

Part 2, "How often does this statement apply to you?" Response options: Always / Often / Sometimes / Rarely / Never.

| # | Item | Points (Always→Never) |
|---|------|------|
| 7 | Giving a gift for a wedding, birthday or other occasion would put a strain on my finances for the month | 0 1 2 3 4 |
| 8 | I have money left over at the end of the month | 4 3 2 1 0 |
| 9 | I am behind with my finances | 0 1 2 3 4 |
| 10 | My finances control my life | 0 1 2 3 4 |

Part 3 asks the respondent's age (18–61 or 62+) and mode ("I read the questions" or "Someone read the questions to me"). Total response value runs from 0 to 40.

#### 5-item version (abbreviated), exact wording
1. Because of my money situation, I feel like I will never have the things I want in life (0–4, reverse)
2. I am just getting by financially (0–4, reverse)
3. I am concerned that the money I have or will save won't last (0–4, reverse)
4. I have money left over at the end of the month (Always=4 … Never=0)
5. My finances control my life (Always=0 … Never=4)

Total response value runs from 0 to 20.

#### Scoring method
- User guide §4.1: "The score used for the CFPB Financial Well-Being Scale is based on an Item Response Theory (IRT) analysis." Practitioners without IRT software "can use a 'lookup' table".
- The process has two steps. Step 1 sums the item points into a total response value. Step 2 converts that total to a score using the column for the respondent's age group (18–61 or 62+) and mode (self-administered or administered by someone else).
- §4.4.1: "A CFPB Financial Well-Being Scale score is a standardized number between 0 and 100".
- "What is a good (or bad) score?": "there is not a specific cut-off for a 'good' or 'bad' financial well-being score." The guide also says "small changes in a person's score should be interpreted carefully."
- **Missing items:** the lookup table needs every item answered. "Questionnaires with missing and 'don't know' responses can still be scored using specialized IRT software."
- **Wording:** "If you make changes to the wording of question items or responses, you will not be able to accurately estimate a respondent's ... score."
- **Software:** scores were generated using flexMIRT®. The 2017 report, footnote 24, says the IRT natural scale (mean 0, SD 1) is converted to "a score distribution that ranges from approximately 0 to 100 and is centered at 50". It also mentions a custom Stata package.
- **Comparability:** the 5-item scores "can be compared directly to the scores from the standard version". The 10-item version has higher reliability and precision.
- **Permission:** "No. This scale and scoring procedure is free and publicly available."

#### Conversion tables (verbatim from the worksheets)

**10-item version.** Columns: self-administered 18–61, self-administered 62+, administered by someone else 18–61, administered by someone else 62+.

| Raw | SA 18–61 | SA 62+ | Other 18–61 | Other 62+ |
|---|---|---|---|---|
| 0 | 14 | 14 | 16 | 18 |
| 1 | 19 | 20 | 21 | 23 |
| 2 | 22 | 24 | 24 | 26 |
| 3 | 25 | 26 | 27 | 28 |
| 4 | 27 | 29 | 29 | 30 |
| 5 | 29 | 31 | 31 | 32 |
| 6 | 31 | 33 | 33 | 33 |
| 7 | 32 | 35 | 34 | 35 |
| 8 | 34 | 36 | 36 | 36 |
| 9 | 35 | 38 | 38 | 38 |
| 10 | 37 | 39 | 39 | 39 |
| 11 | 38 | 41 | 40 | 40 |
| 12 | 40 | 42 | 42 | 41 |
| 13 | 41 | 44 | 43 | 43 |
| 14 | 42 | 45 | 44 | 44 |
| 15 | 44 | 46 | 45 | 45 |
| 16 | 45 | 48 | 47 | 46 |
| 17 | 46 | 49 | 48 | 47 |
| 18 | 47 | 50 | 49 | 48 |
| 19 | 49 | 52 | 50 | 49 |
| 20 | 50 | 53 | 52 | 50 |
| 21 | 51 | 54 | 53 | 52 |
| 22 | 52 | 56 | 54 | 53 |
| 23 | 54 | 57 | 55 | 54 |
| 24 | 55 | 58 | 57 | 55 |
| 25 | 56 | 60 | 58 | 56 |
| 26 | 58 | 61 | 59 | 57 |
| 27 | 59 | 63 | 60 | 58 |
| 28 | 60 | 64 | 62 | 60 |
| 29 | 62 | 66 | 63 | 61 |
| 30 | 63 | 67 | 65 | 62 |
| 31 | 65 | 69 | 66 | 64 |
| 32 | 66 | 71 | 68 | 65 |
| 33 | 68 | 73 | 70 | 67 |
| 34 | 69 | 75 | 71 | 68 |
| 35 | 71 | 77 | 73 | 70 |
| 36 | 73 | 79 | 76 | 72 |
| 37 | 75 | 82 | 78 | 75 |
| 38 | 78 | 84 | 81 | 77 |
| 39 | 81 | 88 | 85 | 81 |
| 40 | 86 | 95 | 91 | 87 |

**5-item version.** Same column order.

| Raw | SA 18–61 | SA 62+ | Other 18–61 | Other 62+ |
|---|---|---|---|---|
| 0 | 19 | 20 | 22 | 24 |
| 1 | 25 | 26 | 30 | 30 |
| 2 | 29 | 31 | 33 | 33 |
| 3 | 32 | 34 | 36 | 37 |
| 4 | 36 | 37 | 39 | 39 |
| 5 | 38 | 40 | 42 | 42 |
| 6 | 41 | 43 | 44 | 44 |
| 7 | 43 | 46 | 47 | 46 |
| 8 | 46 | 48 | 49 | 49 |
| 9 | 48 | 51 | 51 | 51 |
| 10 | 50 | 53 | 54 | 53 |
| 11 | 53 | 55 | 56 | 55 |
| 12 | 55 | 58 | 58 | 57 |
| 13 | 57 | 61 | 60 | 59 |
| 14 | 60 | 63 | 63 | 62 |
| 15 | 63 | 66 | 66 | 64 |
| 16 | 65 | 69 | 68 | 67 |
| 17 | 68 | 73 | 72 | 70 |
| 18 | 72 | 76 | 75 | 73 |
| 19 | 76 | 81 | 80 | 77 |
| 20 | 82 | 90 | 87 | 84 |

The extremes are approximately 14–95 (10-item) and 19–90 (5-item), not literally 0–100.

#### 2017 national survey, *Financial well-being in America*
- **Fielding:** a pilot in October 2016, then the main survey "between October 27 and December 5, 2016". "Overall, 6,394 surveys were completed: 5,395 from the general population sample and 999 from the oversample of those aged 62 or older." Results are weighted.
- **Mean:** "The average financial well-being score for U.S. adults is 54 on a scale that falls between zero and 100." Footnote: "54 is also the median level."
- **Percentiles (Table 3):** mean 54, p10 37, p25 46, p50 54, p75 63, p90 72. "There is a 35-point spread between the 10th and 90th percentiles."
- **Bands (descriptive, not official cut-offs):** "About a third of all adults in the United States have financial well-being scores of 50 or below, about a third have scores between 51 and 60, and about a third have scores of 61 or above." Also "13 percent of scores at or below 40 and 14 percent above 70."
- **What the bands mean:**
  - "Scores of 50 or below are associated with both a high probability (well above 50%) of struggling to make ends meet and of experiencing material hardship."
  - "scores of 61 and above are associated with low probability (less than 10%) of having trouble paying for basic needs or making ends meet."
  - Difficulty making ends meet: "more than nine out of ten" in the 11–40 range, "about 70 percent" in 41–50, "about 30 percent" in 51–60, and "less than one in ten" above 60.
- **Standard deviation for all adults:** **not verified.** It is in Appendix A, which was not extracted.

**Design implication:** the CFPB itself says there is no official good/bad cut-off. If the app shows bands, label them as derived from the 2017 descriptive thirds (≤50, 51–60, ≥61) and not as CFPB thresholds.

---

### B. Financial Health Network FinHealth Score

**Status:** the 8 indicators, point values, equal-weight averaging and tiers (0–39 / 40–79 / 80–100) are verified from the 2021 FinHealth Score Toolkit PDF. The 2026 revised question wording is verified from the March 2026 measurement brief. Of the latest Pulse figures, Healthy 31% and Vulnerable 17% (2026) are verified. The Coping share and the mean score for 2026 are **not verified**. The full report PDFs exceeded the fetch size limit, and direct download was blocked.

#### Sources
- FinHealth Score Toolkit (2021): https://finhealthnetwork.org/wp-content/uploads/2021/11/FinHealthScoreToolkit-2021.pdf
- Methodology / toolkit request page (gated form): https://finhealthnetwork.org/tools/financial-health-score/finhealth-score-methodology/ and https://finhealthnetwork.org/score/
- Revision brief, *From Insight to Impact: The Next Phase of Financial Health Measurement* (March 2026): https://finhealthnetwork.org/wp-content/uploads/2026/03/From-Insight-to-Impact_-The-Next-Phase-of-Financial-Health-Measurement.pdf
- *Financial Health Pulse 2026 U.S. Trends Report* page (Sep 22, 2026): https://finhealthnetwork.org/research/financial-health-pulse-2026-u-s-trends-report/ (PDF: https://finhealthnetwork.org/wp-content/uploads/2026/11/Financial-Health-Pulse-2026-U.S.-Trends-Report-Mounting-Pressure-Growing-Vulnerability-FHN.pdf, not read)
- 2025 Pulse press release (Sep 17, 2025): https://finhealthnetwork.org/financial-health-pulse-2025-financial-health-challenges-persist-student-debt-and-insurance-raise-new-alarms/

#### The 8 indicators and point values (2021 Toolkit, original Score)
The indicators group into four pillars: Spend, Save, Borrow and Plan.

| Indicator | Toolkit question (abridged) | Points |
|---|---|---|
| 1. Spend less than income | Spending vs income over the last 12 months | much less 100 · a little less 75 · about equal 50 · a little more 25 · much more 0 |
| 2. Pay bills on time | "My household has been financially able to:" | all 100 · nearly all 60 · most 40 · some 20 · very few 0 |
| 3. Sufficient liquid savings | How long could you cover expenses on readily available money | 6+ months 100 · 3–5 months 75 · 1–2 months 50 · 1–3 weeks 25 · <1 week 0 |
| 4. Sufficient long-term savings | Confidence in meeting longer-term goals | Very 100 · Moderately 75 · Somewhat 50 · Slightly 25 · Not at all 0 |
| 5. Manageable debt | How manageable is your household debt | no debt 100 · manageable 85 · a bit more than manageable 40 · far more than manageable 0 |
| 6. Prime credit score | "How would you rate your credit score?" | Excellent 100 · Very good 80 · Good 60 · Fair 40 · Poor 0 · I don't know 0 |
| 7. Appropriate insurance | Confidence that insurance will provide enough support | Very 100 · Moderately 75 · Somewhat 50 · Slightly 25 · Not at all 10 · no insurance 0 |
| 8. Plan ahead | "My household plans ahead financially." | Agree strongly 100 · Agree somewhat 65 · Neither 35 · Disagree somewhat 15 · Disagree strongly 0 |

#### Scoring and tiers
- **Overall score:** the toolkit says "Average of point values for QUESTIONS 1 - 8". That is an equal-weight mean of the 8 indicator scores.
- **Sub-scores:** each is the average of its pair. Spend = Q1 and Q2, Save = Q3 and Q4, Borrow = Q5 and Q6, Plan = Q7 and Q8.
- **Tiers, verbatim:**
  - "Financial health scores between 0 - 39 are considered Financially Vulnerable."
  - "Financial health scores between 40 - 79 are considered Financially Coping."
  - "Financial health scores between 80 - 100 are considered Financially Healthy."
  - Healthy means "healthy outcomes across all eight financial health indicators".

#### 2026 revision (brief, March 2026)
- The revised questions keep the same 8 indicators under new names:
  - Spending relative to income
  - On-time bill payment
  - Liquid savings levels
  - Progress on long-term savings goals
  - Debt manageability (a two-part question)
  - Credit score
  - Adequacy of insurance coverage
  - Planning ahead financially
- The brief calls the changes "an enhancement, not a wholesale transformation, of the Score."
- Some response scales changed. For example, liquid savings now uses "Less than 1 month but more than 1 week" and "Less than 1 week".
- The brief says FHN "expect[s] to roll out the revised FinHealth Score survey and scoring methodology in the 2026 Financial Health Pulse".
- **Not verified:** the revised point values. The new scoring is in the gated toolkit.

#### Latest Pulse numbers
- **2026 Trends Report (Sep 22, 2026):** Financially Vulnerable was "17% in 2026, up from 15% in 2025". Financially Healthy was 31%, "virtually unmoved for the fifth consecutive year."
  - Coping 52% is implied by subtraction but **not verified** as stated.
  - The mean score is **not verified**.
  - Other figures from the same page: spending less than income fell from 49% to 47%, paying all bills on time was 68%, and unmanageable debt rose from 29% to 31%.
- **2025 report:** Vulnerable "fell to 15% in 2025". Healthy "has hovered around 30%" from 2018 to 2025. The sample was "more than 7,400 households" from a probability-based panel.

**Design implication:** an app can compute Spend (1–2) and parts of Save (3) from ledger data. Indicators 4–8 need self-report or other data. Treat any ledger-only "FinHealth-like" score as an approximation, not the FinHealth Score®, which is a registered mark under its own licence terms.

---

### C. Federal Reserve SHED

**Status:** verified. The latest edition is *Economic Well-Being of U.S. Households in 2025*, published May 2026, from a survey fielded in October 2025.

- **Savings chapter:** https://www.federalreserve.gov/publications/2026-economic-well-being-of-us-households-in-2025-savings-investments.htm (page shows "Last Update: May 26, 2026")
- **Fact sheet:** https://www.federalreserve.gov/newsevents/pressreleases/files/other20260513a1.pdf (the file name suggests a May 13, 2026 release; exact release date **not verified**)

| Measure (survey year 2025) | Figure | Verbatim |
|---|---|---|
| Would cover a $400 emergency expense with cash or its equivalent | **63%** | "Sixty-three percent of adults said they would cover a hypothetical $400 emergency expense exclusively using cash, savings, or a credit card paid off at the next statement (referred to, altogether, as 'cash or its equivalent'). This share was unchanged from the previous three years, but down from a high of 68 percent in 2021." |
| Unable to pay a $400 expense by any means | **12%** | "12 percent of all adults said they would be unable to pay the expense by any means." |
| Three months of emergency savings | **55%** | "55 percent of adults said they had set aside money for three months of expenses." |
| Doing okay or living comfortably | 73% | fact sheet |
| Had a major unexpected expense in the prior 12 months | 59% | fact sheet |

The prior edition, *in 2024* (published May 28, 2025), also reported 63% for the $400 measure: https://www.federalreserve.gov/publications/2025-economic-well-being-of-us-households-in-2024-executive-summary.htm

---

### D. Ratio benchmarks

**Status:** mixed. See the status column.

| Benchmark | What the source actually says | Source | Status |
|---|---|---|---|
| Emergency fund 3–6 months | FINRA: "Financial planners often recommend the equivalent of three to six months of living expenses." It adds that people with variable income may need more. | https://www.finra.org/investors/personal-finance/start-emergency-fund | Verified |
| CFPB on emergency funds | The CFPB gives **no** month target: "The amount you need to have in an emergency savings fund depends on your situation." | https://www.consumerfinance.gov/an-essential-guide-to-building-an-emergency-fund/ | Verified. Do **not** attribute "3–6 months" to the CFPB. |
| Academic liquidity / savings / debt ratios | See the paper details below this table. | Greninger, Hampton, Kitt & Achacoso (1996) | Verified |
| QM 43% DTI → price-based | See the rule details below this table. | Final rule, 85 FR (Dec 29, 2020): https://www.govinfo.gov/content/pkg/FR-2020-12-29/html/2020-27567.htm · CFPB PDF https://files.consumerfinance.gov/f/documents/cfpb_atr-qm-general-qm-final-rule_2020-12.pdf · delay rule (Apr 30, 2021): https://www.govinfo.gov/content/pkg/FR-2021-04-30/pdf/2021-09028.pdf | Verified. The October 1, 2022 date comes from search summaries of the delay rule, not a direct read. |
| CFPB DTI definition | "Your debt-to-income ratio (DTI) is all your monthly debt payments divided by your gross monthly income." The page gives **no** threshold and says "Different loan products and lenders will have different DTI limits." | https://www.consumerfinance.gov/ask-cfpb/what-is-a-debt-to-income-ratio-en-1791/ | Verified |
| "28/36" back-end DTI rule of thumb | No primary source found | | **Not verified.** Use Greninger's 35%/45% after-tax figures instead. |
| Credit utilization under 30% | myFICO: "Some financial experts recommend keeping your credit utilization ratio below 30%", and "the data doesn't support the implication that your credit score will dip" above it. It also says "generally the lower your ratio is, the better". | https://www.myfico.com/credit-education/blog/credit-utilization-be | Verified |
| Credit utilization, Experian | "30% is the point at which it starts to have a more pronounced negative effect on your credit score". The highest scorers are "in the low single digits", with an average of 7.1% for 800–850 (Q3 2024 data, page dated Oct 9, 2025). | https://www.experian.com/blogs/ask-experian/credit-education/score-basics/credit-utilization-rate/ | Verified |
| CFPB on the 30% figure | No CFPB primary page with a 30% rule was found | | **Not verified** |
| FICO "Amounts owed" weight | The category, which includes utilization, is about 30% of a FICO Score. This is a weight and is unrelated to the 30% utilization rule of thumb. | https://www.myfico.com/credit-education/amounts-owed/ (search-result level) | Partially verified |
| 50/30/20 | See the book details below this table. | Warren & Tyagi, *All Your Worth* (2005) | Partially verified |

#### Greninger et al. (1996)
"Ratios and Benchmarks for Measuring the Financial Well-Being of Families and Individuals," *Financial Services Review* 5(1):57–70, DOI 10.1016/S1057-0810(96)90027-X (Crossref-verified). Free PDF: https://openjournals.libs.uga.edu/fsr/article/download/3790/3237

- **Method:** a Delphi study of 156 experts, 85 planners and 71 educators.
- **Liquidity:** liquid assets ÷ monthly expenses had a median of **300%**. "liquid assets should provide a minimum of 2½ to 3 months of living expenses." Table 2 recommends **≥250%** (≥2.5× monthly expenses).
- **Savings:** savings ÷ gross income had a median of **10%**, and Table 2 recommends **≥10%**.
- **Housing:** reasonable renter's expenses ÷ gross income is **≤30%**. Homeowner's expenses ÷ gross income is **≤35%**.
- **Non-mortgage debt payments ÷ after-tax income:**
  - Reasonable median 10%. The Table 2 figure is ≤15%, but the OCR is unclear, so this reading is **not verified**.
  - Danger point median **20%**.
- **Total debt payments ÷ after-tax income:** reasonable median **35%**, danger point median **45%**.
- **Total expenses ÷ after-tax income:** reasonable 80%, danger 90%.
- **Attribution:** "DeVaney/Greninger 1996" is a conflation. DeVaney is not an author. The related paper is DeVaney (1994), "The usefulness of financial ratios as predictors of household insolvency," *Financial Counseling and Planning* 5:15–24 (seen in search results; details **not verified**).

#### QM 43% DTI rule change
- Summary: the rule "removes the General QM loan definition's 43 percent DTI limit". It qualifies a loan if its APR "exceeds APOR for a comparable transaction by less than 2.25 percentage points", with higher thresholds for smaller loans and subordinate liens.
- Creditors are still "required to consider the consumer's DTI ratio or residual income".
- The rule was effective **March 1, 2021**, with an original mandatory compliance date of July 1, 2021. That date was later delayed to **October 1, 2022**.
- **Not verified:** the $110,260 small-loan figure.

#### 50/30/20
- Search results consistently attribute it to Elizabeth Warren and Amelia Warren Tyagi, *All Your Worth: The Ultimate Lifetime Money Plan* (2005). They call it the "Balanced Money Formula": 50% must-haves, 30% wants, 20% savings, all of after-tax income.
- The book was not read. The MONEY / CNN 2005 article (https://money.cnn.com/magazines/moneymag/moneymag_archive/2005/08/01/8267041/index.htm) returned HTTP 503.
- Cite it as "popularized by". Several sources note the origin is disputed, and one source gives 2006.
- **Not verified:** exact wording from the book.

---

### E. JPMorgan Chase Institute (JPMCI)

**Status:** verified from the JPMCI report pages. The title "Sizing the cash buffer" was **not found** as a report title. The ~6-week finding is from *Weathering Volatility 2.0*.

| Report | Date | Key numbers (from the JPMCI page) | URL |
|---|---|---|---|
| *Weathering Volatility: Big Data on the Financial Ups and Downs of U.S. Individuals* | May 2015 | See the details below this table. | https://www.jpmorganchase.com/institute/all-topics/household-financial-health/report-weathering-volatility |
| *Paychecks, Paydays, and the Online Platform Economy: Big Data on Income Volatility* | Feb 2016 | See the details below this table. | https://www.jpmorganchase.com/institute/all-topics/labor-markets-and-workforce/report-paychecks-paydays-and-the-online-platform-economy |
| *Weathering Volatility 2.0: A Monthly Stress Test to Guide Savings* | Oct 23, 2019 | See the details below this table. | https://www.jpmorganchase.com/institute/all-topics/household-financial-health/report-weathering-volatility-2-a-monthly-stress-test-to-guide-saving · exec summary PDF https://ir.jpmorganchase.com/content/dam/jpmc/jpmorgan-chase-and-co/institute/pdf/institute-volatility-cash-buffer-executive-summary.pdf |

#### Weathering Volatility (May 2015)
- **Data:** October 2012 to December 2014, an analysis sample of 100,000 people.
- 84% had month-to-month income changes above 5%.
- A typical middle-income household needed about **$4,800** in liquid assets, about **14%** of annual after-tax income, against a median of $3,000 held.

#### Paychecks, Paydays, and the Online Platform Economy (Feb 2016)
- **Data:** October 2012 to September 2015, 1 million customers.
- Share with a >30% month-to-month income change: **55%** nationally, 70% of 18–24s, 74% of the bottom income quintile.
- "The typical person experienced dips in income 43 percent of the time and spikes in income 33 percent of the time".
- The mean monthly change in labor income was $475 for median-income earners.

#### Weathering Volatility 2.0 (Oct 23, 2019)
- **Data:** 6 million families, October 2012 to December 2018.
- **Simultaneous income dip and spending spike:** occurs "once every 5.5 years" and needs a **6.2 weeks** median buffer of take-home income. **65%** of families lack it.
- **Income dip alone:** every 9 months, needs 2.8 weeks, and 48% lack it.
- **Spending spike alone:** every 4 months, needs 2.6 weeks, and 46% lack it.
- Median income volatility is about 36% month-to-month.

**Design implication:** "about six weeks of take-home pay" is a defensible, bank-data-derived buffer target. It suits a ledger-based app better than a survey benchmark. Cite it as JPMCI (2019) and say it is a median.

---

### F. Plaid API

**Status:** verified from plaid.com/docs, fetched 2026-11-24. The taxonomy was checked against Plaid's official CSV.

#### /transactions/sync
https://plaid.com/docs/api/products/transactions/

- **cursor:** "The cursor value represents the last update requested." Omitting it returns "the entire history of updates ... starting with the first-added transactions on the Item."
- **added / modified / removed:** each is the set of transactions added, modified or removed "since cursor ordered by ascending last modified time."
- **has_more:** "Represents if more than requested count of transaction updates exist." Keep paging.
- **next_cursor:** "Cursor used for fetching any future updates after the latest update provided in this response."
- **Pagination error:** on `TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION`, "the entire pagination request loop must be restarted beginning with the cursor for the first page of the update."
- **count:** default 100, minimum 1, maximum 500.
- **amount sign:** "Positive values when money moves out of the account; negative values when money moves in."
  - Investments transactions follow the same direction: "Inflows are negative and outflows are positive" (https://plaid.com/docs/investments/).

#### Pending → posted
https://plaid.com/docs/transactions/transactions-data/

- "It typically takes about one to five business days for a transaction to move from pending to posted". It can take up to 14 days in rare cases.
- **With sync:** the pending transaction appears in `removed` and the posted one in `added`. They may land on different pages but arrive in the same update.
- **Linkage:** "the pending transaction's id will be marked in the `pending_transaction_id` of the posted transaction." It is `null` if there is no match.
  - API reference: "The ID of a posted transaction's associated pending transaction, where applicable."
- "a pending transaction may not convert to a posted transaction at all and will simply disappear."
- Amounts can change between pending and posted (for example, tips).
- **Legacy /transactions/get:** Plaid "removes the pending transaction, sends a TRANSACTIONS_REMOVED webhook, and returns the new, posted transaction."

#### personal_finance_category (PFC)
- **Fields:** `primary`, `detailed`, `confidence_level` and `version`.
  - `confidence_level` takes the values `VERY_HIGH`, `HIGH`, `MEDIUM`, `LOW` and `UNKNOWN`.
  - `version` takes `v1` or `v2`. It is requested with `options.personal_finance_category_version`.
- **Which version you get:** Items or customers enabled before **December 3, 2025** default to v1 and may opt in to v2. Those enabled on or after that date receive v2 only.
- **Official CSV:** https://plaid.com/documents/pfc-taxonomy-all.csv. Its note: "PFCv2 is a superset of PFCv1. All new customers after December 2025 are on PFCv2."
- **Migration guide:** https://plaid.com/docs/transactions/pfc-migration/
- **v2 primaries (18), parsed from the CSV:**
  INCOME, LOAN_DISBURSEMENTS, LOAN_PAYMENTS, TRANSFER_IN, TRANSFER_OUT, BANK_FEES, ENTERTAINMENT, FOOD_AND_DRINK, GENERAL_MERCHANDISE, HOME_IMPROVEMENT, MEDICAL, PERSONAL_CARE, GENERAL_SERVICES, GOVERNMENT_AND_NON_PROFIT, TRANSPORTATION, TRAVEL, RENT_AND_UTILITIES, OTHER.
- **v1 primaries (16):** the same list without LOAN_DISBURSEMENTS and OTHER.
- **Detailed codes:** 127 in v2 and 105 in v1, as CSV rows.
- **New in v2 (detailed):**
  - INCOME_SALARY, INCOME_CONTRACTOR, INCOME_GIG_ECONOMY, INCOME_RENTAL, INCOME_CHILD_SUPPORT, INCOME_LONG_TERM_DISABILITY, INCOME_MILITARY, INCOME_OTHER
  - LOAN_DISBURSEMENTS_* (AUTO, CASH_ADVANCES, EWA, MORTGAGE, PERSONAL, STUDENT, OTHER_DISBURSEMENT)
  - LOAN_PAYMENTS_BNPL, LOAN_PAYMENTS_CASH_ADVANCES, LOAN_PAYMENTS_EWA
  - TRANSFER_IN_WIRE, TRANSFER_IN_TRANSFER_IN_FROM_APPS, TRANSFER_OUT_WIRE, TRANSFER_OUT_CRYPTO, TRANSFER_OUT_TRANSFER_OUT_FROM_APPS
  - BANK_FEES_LATE_FEES, BANK_FEES_CASH_ADVANCE, OTHER_OTHER
- **v1-only (detailed):** INCOME_WAGES, INCOME_OTHER_INCOME, TRANSFER_IN_CASH_ADVANCES_AND_LOANS.
  - INCOME_WAGES is v1 ("Income from salaries, gig-economy work, and tips earned"). The v2 equivalent is INCOME_SALARY ("Income from salaries and wages").
- **Unchanged codes:** `TRANSFER_IN_ACCOUNT_TRANSFER` ("General inbound transfers from another account"), `TRANSFER_OUT_ACCOUNT_TRANSFER` and `LOAN_PAYMENTS_CREDIT_CARD_PAYMENT` ("Payments made to a credit card account.") are the same in v1 and v2.

#### /transactions/recurring/get
https://plaid.com/docs/api/products/transactions/

- **Returns** `inflow_streams` and `outflow_streams`. Each stream has `stream_id`, `average_amount`, `last_amount`, `is_active` and `predicted_next_date`. `predicted_next_date` is nullable: "only be set if the next payment date can be predicted".
- **frequency:** `UNKNOWN`, `WEEKLY`, `BIWEEKLY`, `SEMI_MONTHLY`, `MONTHLY`, `ANNUALLY`.
- **status values:**
  - `MATURE`: at least 3 transactions, or 2 for annual streams.
  - `EARLY_DETECTION`
  - `TOMBSTONED`: was early detection and missed its next expected date.
  - `UNKNOWN`
- **History:** request at least **180 days** (`days_requested`). Call the endpoint after `SYNC_UPDATES_AVAILABLE` arrives with `historical_update_complete: true`.
- **Billing:** "Recurring Transactions" is a subscription product (billing page).

#### /liabilities/get
https://plaid.com/docs/api/products/liabilities/ and https://plaid.com/docs/liabilities/

- **Credit fields:**
  - `aprs[]` with `apr_percentage`, `apr_type` (`balance_transfer_apr`, `cash_apr`, `penalty_apr`, `purchase_apr`, `special`), `balance_subject_to_apr` and `interest_charge_amount`. The array is empty if the issuer gives no APR data.
  - `is_overdue` ("Availability for this field is limited")
  - `last_payment_amount` and `last_payment_date`
  - `last_statement_balance` ("The total amount owed as of the last statement issued.") and `last_statement_issue_date`
  - `minimum_payment_amount` ("The minimum payment due for the next billing cycle.")
  - `next_payment_due_date` ("The due date is `null` if a payment is not expected."). All of these are nullable.
- **Student loan fields:** `loan_status`, `repayment_plan`, `interest_rate_percentage`, `minimum_payment_amount`, `next_payment_due_date`, `expected_payoff_date`, `outstanding_interest_amount`, YTD interest and principal, and others.
- **Mortgage fields:** `interest_rate{percentage,type}`, `next_monthly_payment`, `next_payment_due_date`, `past_due_amount`, `escrow_balance`, `has_pmi`, `maturity_date`, and others.
- **Supported accounts:** `credit` (credit card, paypal) and `loan` (auto, commercial, consumer, home equity, line of credit, mortgage, student, and others).
- **Refresh:** "Liabilities data is refreshed approximately once per day." Billed as a subscription.
- **Sandbox:** Liabilities "can be tested in Sandbox without extra permissions". The test-credentials page points to `user_loan_loc` / `pass_good`. Whether `user_good` returns credit-card liabilities with due dates is **not verified**. This is relevant to the handoff's open question.
- **Adding to an existing Item:** the Liabilities overview only describes listing `liabilities` in `products` at Link. Update-mode and `optional_products` guidance was **not verified** on that page.

#### Balances
- **`/accounts/get`:** "The balance returned will reflect the balance at the time of the last successful Item update" (cached). https://plaid.com/docs/api/accounts/
- **Real-time:** "Balance always performs a real-time data extraction", with p50 about 3s and p95 about 11s. https://plaid.com/docs/balance/
  - `/accounts/balance/get` "is recommended for all use cases where you are not checking balance to evaluate a proposed ACH transaction". `/signal/evaluate` is preferred for ACH risk.
  - Balance is billed per request.
- **available:** "The amount of funds available to be withdrawn from the account, as determined by the financial institution" (nullable).
- **current:** "The total amount of funds in or owed by the account." For credit accounts, a positive balance means the amount owed.
- **limit:** "For credit-type accounts, this represents the credit limit."
- **Account types and subtypes:**
  - depository: cash management, cd, checking, ebt, hsa, money market, paypal, prepaid, savings, and others
  - credit: credit card, paypal
  - loan: 14 subtypes
  - investment: about 60 subtypes, including 401k, ira, roth and brokerage
  - other

#### Investments
https://plaid.com/docs/investments/
- The endpoints are `/investments/holdings/get` (holdings and securities, with cost basis and `tax_lots`) and `/investments/transactions/get` (up to 24 months).
- Billed as a subscription. Refresh is billed per request.

#### Pricing and environments
https://plaid.com/docs/account/billing/ and https://plaid.com/docs/sandbox/
- **Sandbox:** "a free and fully-featured environment", with unlimited test Items. Credentials are `user_good` / `pass_good`. `user_transactions_dynamic` gives realistic, refreshable transactions.
- **Subscription** (billed monthly while a valid access_token exists, ended by `/item/remove`): Transactions, Recurring Transactions, Liabilities and Investments.
- **Per request:** Balance.
- **Trial plan:** free for new US/Canada teams created on or after **April 15, 2026**, with **10 Production Items**. Deleting Items does not free up slots.
- **Limited Production:** "has been replaced by Trial plans" for teams created on or after that date. Whether older teams keep Limited Production is **not verified**.
- **Per-unit prices:** not public ("apply for Production access or contact sales").

---

### G. Forecasting references

**Status:** verified. Hyndman & Athanasopoulos, *Forecasting: Principles and Practice*, 3rd ed., OTexts, free at https://otexts.com/fpp3/. The site now uses one page per chapter, and the old section URLs 301-redirect to anchors.

| Topic | Section and URL | Key content |
|---|---|---|
| Seasonal naïve | §5.2 https://otexts.com/fpp3/toolbox.html#sec-simple-methods | "we set each forecast to be equal to the last observed value from the same season". ŷ_{T+h\|T} = y_{T+h−m(k+1)} |
| Prediction intervals | §5.5 https://otexts.com/fpp3/toolbox.html#sec-prediction-intervals | ŷ ± c·σ̂_h, where c = 1.28 (80%) and 1.96 (95%). Benchmark σ̂_h: naïve σ̂√h, seasonal naïve σ̂√(k+1), mean σ̂√(1+1/T), drift σ̂√(h(1+h/(T−1))). Bootstrap when residuals are non-normal. |
| Simple exponential smoothing | §8.1 https://otexts.com/fpp3/exponential-smoothing.html#sec-ses | A weighted average where "the weights decrease exponentially as observations come from further in the past" |
| Holt-Winters | §8.3 https://otexts.com/fpp3/exponential-smoothing.html (holt-winters anchor) | Additive is "preferred when the seasonal variations are roughly constant". Multiplicative is for variations "changing proportional to the level". Damped multiplicative "often provides accurate and robust forecasts for seasonal data". φ is typically 0.8–0.98. |
| ETS | §8.5–8.7 https://otexts.com/fpp3/exponential-smoothing.html#sec-ets | ETS(Error, Trend, Seasonal), selected by AICc. Multiplicative-error models are used only for strictly positive data, since they are unstable with zeros or negatives. This matters for sparse expense series. |
| ARIMA | Ch. 9 https://otexts.com/fpp3/arima.html (seasonal ARIMA: #sec-seasonal-arima) | n/a |
| Short series | §13.7 https://otexts.com/fpp3/practical.html#sec-long-short-ts | See the quotes below this table. |
| Count / intermittent data, Croston | §13.2 https://otexts.com/fpp3/practical.html#sec-counts | See the details below this table. |

#### Short series (§13.7)
- Textbook minimums are "misleading and unsubstantiated in theory or practice".
- "There is, for example, no justification for the magic number of 30 often given as a minimum for ARIMA modelling."
- "The only theoretical limit is that we need more observations than there are parameters in our forecasting model."
- See also Hyndman's blog post "Fitting models to short time series" (4 Mar 2014), https://robjhyndman.com/hyndsight/short-time-series/: seasonality "usually takes up m-1 degrees of freedom".

#### Count and intermittent data, Croston (§13.2)
- The method smooths non-zero demand sizes and inter-demand intervals separately with SES. The forecast is their ratio.
- "Forecasts obtained from Croston's method are also known to be biased" (Syntetos & Boylan 2001).
- "There are no algebraic results allowing us to compute prediction intervals for this method."

#### "Seasonal models need ≥2 full seasonal cycles"
- This is **not stated in FPP3**. It is verified as an implementation constraint in R's `stats` source (https://github.com/wch/r-source):
  - `HoltWinters.R`: `stop ("need at least 2 periods to compute seasonal start values")`
  - `decompose.R`: `stop("time series has no or less than 2 periods")` when `length(na.omit(x)) < 2 * f`
  - `stl.R`: `stop("series is not periodic or has less than two periods")`
- For monthly data with yearly seasonality, that means at least 24 months before any seasonal model. Cite it as a software constraint, not a textbook rule.

#### Croston citation
Croston, J. D. (1972). "Forecasting and Stock Control for Intermittent Demands." *Operational Research Quarterly* 23(3):289–303. DOI **10.1057/jors.1972.50**. Crossref-verified; Crossref lists the journal under its current name, *Journal of the Operational Research Society*.

---

### H. AI financial-advice guidance and Section 1033

#### CFPB, *Chatbots in Consumer Finance* (Issue Spotlight, June 6, 2023)
**Status:** verified via the CFPB newsroom.
- PDF: https://files.consumerfinance.gov/f/documents/cfpb_chatbot-issue-spotlight_2023-06.pdf
- Newsroom: https://www.consumerfinance.gov/about-us/newsroom/cfpb-issue-spotlight-analyzes-artificial-intelligence-chatbots-in-banking/
- About 37% of the U.S. population is estimated to have interacted with a bank chatbot in 2022.
- The risks it names:
  - noncompliance with federal consumer financial law (inaccurate information, failure to recognise when a consumer invokes a legal right, privacy and data-security failures)
  - diminished customer service and trust, including "doom loops" and no path to a human
  - consumer harm
- Institutions should use chatbots "in a manner consistent with their customer and legal obligations".

#### FINRA
- **Regulatory Notice 24-09 (June 27, 2024):** reminds firms that existing rules apply to generative AI and LLMs, and that rules are "technology neutral". It highlights Rule 2210 (communications) and Rule 3110 (supervision). It states it creates no new requirements. https://www.finra.org/rules-guidance/notices/24-09 (search-summary level)

#### SEC
- **IM Guidance Update 2017-02 on robo-advisers** and an Investor Bulletin (Feb 23, 2017) cover disclosure, suitability and compliance under the Advisers Act. https://www.sec.gov/investment/im-guidance-2017-02.pdf (search-summary level)
- **Predictive data analytics proposal (2023):** would have required advisers and broker-dealers to neutralise conflicts from "covered technologies", including AI. It was **withdrawn June 12, 2025** along with 13 other proposals (law-firm summaries, e.g. https://www.ropesgray.com/en/insights/alerts/2025/06/sec-clears-unfinished-rulemakings-from-regulatory-agenda). The original proposal date (July 26 vs Aug 9, 2023) is **not verified**.
- **Relevance:** a budgeting app that gives no securities recommendations is generally outside SEC and FINRA adviser rules. These documents are best-practice references, not obligations. That applicability judgement is **not legal advice and not verified**.

#### NIST
- **AI RMF 1.0 (NIST AI 100-1), Jan 26, 2023:** https://www.nist.gov/itl/ai-risk-management-framework, DOI 10.6028/NIST.AI.100-1. It is voluntary and built on four functions: Govern, Map, Measure and Manage.
- **Generative AI Profile (NIST AI 600-1), July 2024:** https://nvlpubs.nist.gov/nistpubs/ai/NIST.AI.600-1.pdf. It lists 12 GenAI risks, including confabulation, data privacy and information integrity.
- Both are search-summary level. Any 2025–2026 revision status is **not verified**.

#### CFPB Personal Financial Data Rights rule (Section 1033), status as of late 2026
**Status:** core facts verified via the Federal Register API and secondary legal sources.
- **Final rule:** "Required Rulemaking on Personal Financial Data Rights," published Nov 18, 2024 (FR Doc 2024-25079). https://www.federalregister.gov/documents/2024/11/18/2024-25079/required-rulemaking-on-personal-financial-data-rights
  - The first compliance date was April 1, 2026, for the largest data providers (Cozen O'Connor alert, Apr 9, 2026).
  - Industry standard-setting rule: June 11, 2024 (FR Doc 2024-12658).
- **ANPR:** "Personal Financial Data Rights Reconsideration," published Aug 22, 2025 (FR Doc 2025-16139). https://www.federalregister.gov/documents/2025/08/22/2025-16139/personal-financial-data-rights-reconsideration
  - It covers four issues: who can be a "representative", fees for access, data-security threats, and data-privacy threats.
- **Injunction, Oct 29, 2025:** *Forcht Bank, N.A. v. CFPB*, E.D. Ky., No. 5:24-cv-00304. The CFPB is enjoined from enforcing the rule until it completes reconsideration, and compliance dates are effectively stayed.
  - Coverage: ABA Banking Journal https://bankingjournal.aba.com/2025/11/kentucky-federal-court-enjoins-cfpb-from-enforcing-current-1033-final-rule/ and order https://storage.courtlistener.com/recap/gov.uscourts.kyed.106299/gov.uscourts.kyed.106299.90.0.pdf (not opened)
- **Proposed rule at OIRA:** a "Personal Financial Data Rights Reconsideration" NPRM was sent to OIRA in early August 2026 (Consumer Finance Monitor, Aug 6, 2026, https://www.consumerfinancemonitor.com/2026/08/06/cfpb-sends-new-section-1033-open-banking-proposal-to-oira-for-review/). Its content was not public.
- **As of 2026-11-24:** the Federal Register API shows **no** 2026 proposed or final 1033 rule published, only the Aug 14, 2026 regulatory agenda.
- **Bottom line:** the 2024 rule is on the books but enjoined. A replacement proposal is pending and unpublished. Whether fees for data access will be allowed is unknown.
- **Relevance:** Plaid access is contractual today and does not depend on 1033 compliance.

---

### Not verified (summary)
- **CFPB scale:** the standard deviation (Appendix A not extracted), the technical report contents, and the 2017→2020 "54→55" figure.
- **FinHealth:**
  - the Coping share and mean score for 2026
  - the exact 2025 Healthy share
  - the revised 2026 point values (gated toolkit)
  - whether the 2026 Pulse actually used the revised scoring
- **Ratio rules:** a primary source for the "28/36" rule; a CFPB statement of a 30% utilization rule; the $110,260 QM small-loan figure.
- **50/30/20:** exact wording in *All Your Worth* (the book was not read and the CNN article returned 503).
- **JPMCI:** the title "Sizing the cash buffer".
- **Plaid:**
  - whether Sandbox `user_good` returns credit-card liabilities with due dates
  - how to add Liabilities to an existing Item
  - whether pre-April-2026 teams keep Limited Production
  - per-unit prices
- **SHED:** the exact release date of the 2026 edition.
- **Search-summary level only:** FINRA 24-09, SEC 2017 robo guidance, NIST documents, and the SEC predictive data analytics proposal date.
