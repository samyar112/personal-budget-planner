import { useEffect, useState } from "react";
import { ApiError } from "../api/auth";
import { fetchPayloadPreview, type AiPayload, type AiPayloadPreview, type PrivacyExample } from "../api/ai";
import { categoryLabel, formatDate, formatMoney } from "../api/transactions";
import "./Privacy.css";

type State = { status: "loading" } | { status: "ready"; preview: AiPayloadPreview } | { status: "error"; message: string };

const METRIC_LABELS: Record<string, string> = {
  income_monthly_mean: "Income / month",
  spending_monthly_mean: "Spending / month",
  net_cash_flow_mean: "Left over / month",
  savings_rate: "Savings rate",
};

const CADENCE_LABELS: Record<string, string> = {
  weekly: "Weekly",
  biweekly: "Every 2 weeks",
  monthly: "Monthly",
  quarterly: "Quarterly",
  yearly: "Yearly",
};

function formatMetric(value: number | null, unit: string): string {
  if (value === null) return "Not enough data";
  return unit === "ratio" ? `${(value * 100).toFixed(1)}%` : formatMoney(value);
}

/**
 * The privacy preview: what the AI would receive, what stayed behind, and the checks that ran.
 * Read-only. Nothing is sent to any AI provider from this page.
 */
const Privacy = () => {
  const [state, setState] = useState<State>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setState({ status: "loading" });
      try {
        const preview = await fetchPayloadPreview();
        if (!cancelled) setState({ status: "ready", preview });
      } catch (err) {
        if (!cancelled) {
          setState({
            status: "error",
            message: err instanceof ApiError ? err.message : "Could not load the privacy preview. Try again.",
          });
        }
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  if (state.status === "loading") {
    return (
      <div className="card glass-card dashboard-card">
        <div className="card-body privacy-state" role="status">
          <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" /> Loading…
        </div>
      </div>
    );
  }

  if (state.status === "error") {
    return (
      <div className="card glass-card dashboard-card">
        <div className="card-body privacy-state" role="alert">
          {state.message}
          <button type="button" className="btn btn-sm btn-outline-dark ms-3" onClick={() => setAttempt((n) => n + 1)}>
            Retry
          </button>
        </div>
      </div>
    );
  }

  const { payload, report } = state.preview;
  const withheldTotal = report.withheld.reduce((sum, field) => sum + field.count, 0);
  const checksPassed = report.checks.filter((check) => check.passed).length;

  return (
    <div className="privacy-page">
      {/* 1. Summary */}
      <section className="card glass-card dashboard-card privacy-hero">
        <div className="card-body">
          <div className="privacy-hero__icon" aria-hidden="true">
            <ShieldIcon />
          </div>
          <div>
            <h4 className="mb-1">What the AI sees</h4>
            <p className="card-section-sub mb-3">
              This is everything that would leave VaultBudget for AI analysis. Nothing has been sent.
            </p>
            <div className="privacy-badges">
              <span className={`privacy-badge ${report.passed ? "privacy-badge--ok" : "privacy-badge--blocked"}`}>
                {report.passed ? "✓ Privacy check passed" : "✕ Blocked: nothing will be sent"}
              </span>
              <span className="privacy-badge">{withheldTotal} identifying items kept private</span>
              <span className="privacy-badge">
                {payload.coverage.monthsComplete > 0
                  ? `Based on ${payload.coverage.monthsComplete} complete month${payload.coverage.monthsComplete === 1 ? "" : "s"}`
                  : "No complete months of data yet"}
              </span>
            </div>
          </div>
        </div>
      </section>

      {/* 2. Before and after */}
      <section className="card glass-card dashboard-card">
        <div className="card-body">
          <h5 className="privacy-section-title">How your data is reduced</h5>
          <p className="card-section-sub">
            Your real transactions on the left. The AI only ever gets the category on the right, folded into a monthly
            total.
          </p>
          {report.examples.length === 0 ? (
            <div className="privacy-state">No transactions yet. Link a bank to see examples.</div>
          ) : (
            <div className="privacy-examples">
              {report.examples.map((example) => (
                <ExampleRow key={`${example.date}|${example.description}|${example.amount}`} example={example} />
              ))}
            </div>
          )}
        </div>
      </section>

      {/* 3. Kept private */}
      <section className="card glass-card dashboard-card">
        <div className="card-body">
          <h5 className="privacy-section-title">Kept private</h5>
          <p className="card-section-sub">Never copied into what the AI receives.</p>
          <div className="privacy-withheld">
            {report.withheld.map((field) => (
              <div className="privacy-withheld__tile" key={field.field}>
                <span className="privacy-withheld__count">{field.count}</span>
                <span className="privacy-withheld__label">{field.field}</span>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* 4. Checks */}
      <section className="card glass-card dashboard-card">
        <div className="card-body">
          <h5 className="privacy-section-title">
            Privacy checks{" "}
            <span className="privacy-section-count">
              {checksPassed} of {report.checks.length} passed
            </span>
          </h5>
          <p className="card-section-sub">
            Run on the final payload. If any check finds something, the AI call is refused.
          </p>
          <ul className="privacy-checks">
            {report.checks.map((check) => (
              <li key={check.id} className={check.passed ? "is-passed" : "is-failed"}>
                <span className="privacy-checks__mark" aria-hidden="true">
                  {check.passed ? "✓" : "✕"}
                </span>
                <span className="privacy-checks__label">{check.label}</span>
                <span className="privacy-checks__result">
                  {check.passed ? "None found" : `${check.hits} found`}
                </span>
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* 5. What is sent */}
      <SentSection payload={payload} />

    </div>
  );
};

const ExampleRow = ({ example }: { example: PrivacyExample }) => (
  <div className="privacy-example">
    <div className="privacy-example__before">
      <span className="privacy-example__tag">Your ledger</span>
      <span className="privacy-example__desc">{example.description}</span>
      <span className="privacy-example__meta">
        {example.accountName}
        {example.mask && <> ••{example.mask}</>} · {formatDate(example.date)} · {formatMoney(example.amount)}
      </span>
      <span className="privacy-example__private">
        <s>description</s> <s>account</s> {example.mask && <s>number ending</s>} <s>exact date</s>
      </span>
    </div>
    <div className="privacy-example__arrow" aria-hidden="true">
      →
    </div>
    <div className="privacy-example__after">
      <span className="privacy-example__tag">Sent to AI</span>
      <span className="privacy-chip">{categoryLabel(example.sentAs)}</span>
      <span className="privacy-example__meta">Only as part of a monthly total</span>
    </div>
  </div>
);

const SentSection = ({ payload }: { payload: AiPayload }) => {
  const largest = Math.max(...payload.categories.map((category) => category.monthlyMean), 0);

  return (
    <section className="card glass-card dashboard-card">
      <div className="card-body">
        <h5 className="privacy-section-title">What is sent</h5>
        <p className="card-section-sub">
          Monthly averages as of {formatDate(payload.asOf)}
          {payload.coverage.accountTypes.length > 0 && <> · from {payload.coverage.accountTypes.join(", ")} accounts</>}
        </p>

        <div className="privacy-metrics">
          {payload.metrics.map((metric) => (
            <div className="privacy-metric" key={metric.id}>
              <span className="privacy-metric__label">{METRIC_LABELS[metric.id] ?? metric.id}</span>
              <span className="privacy-metric__value">{formatMetric(metric.value, metric.unit)}</span>
            </div>
          ))}
        </div>

        <div className="privacy-sent-grid">
          <div>
            <h6 className="privacy-subtitle">Spending by category</h6>
            {payload.categories.length === 0 ? (
              <div className="privacy-empty">None yet</div>
            ) : (
              <ul className="privacy-bars">
                {payload.categories.map((category) => (
                  <li key={category.code}>
                    <div className="privacy-bars__row">
                      <span>{categoryLabel(category.code)}</span>
                      <span className="privacy-bars__amount">{formatMoney(category.monthlyMean)}</span>
                    </div>
                    <div className="privacy-bars__track">
                      <div
                        className="privacy-bars__fill"
                        style={{ width: `${largest > 0 ? Math.max((category.monthlyMean / largest) * 100, 2) : 0}%` }}
                      />
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div>
            <h6 className="privacy-subtitle">Recurring charges</h6>
            {payload.recurring.length === 0 ? (
              <div className="privacy-empty">None detected</div>
            ) : (
              <ul className="privacy-recurring">
                {payload.recurring.map((stream, index) => (
                  <li key={`${stream.category}|${stream.cadence}|${index}`}>
                    <span>
                      {categoryLabel(stream.category)}
                      {stream.isSubscription && <span className="bill-badge">Subscription</span>}
                      <span className="privacy-recurring__cadence">{CADENCE_LABELS[stream.cadence] ?? stream.cadence}</span>
                    </span>
                    <span className="privacy-bars__amount">{formatMoney(stream.monthlyCost)}/mo</span>
                  </li>
                ))}
              </ul>
            )}
            <p className="privacy-note">Merchant names are not sent.</p>
          </div>
        </div>
      </div>
    </section>
  );
};

const ShieldIcon = () => (
  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    <path d="M12 3 4.5 6v5.5c0 4.6 3.2 8.4 7.5 9.5 4.3-1.1 7.5-4.9 7.5-9.5V6L12 3Z" />
    <path d="m8.8 12 2.2 2.2 4.2-4.4" />
  </svg>
);

export default Privacy;
