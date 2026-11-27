import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ApiError, fetchCurrentUser } from "../api/auth";
import { openBillingPortal, startCheckout } from "../api/billing";
import { generateTips, type AiTipsResponse, type RoadmapStep } from "../api/ai";
import { formatMoney } from "../api/transactions";

type State =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "ready"; result: AiTipsResponse }
  | { status: "error"; message: string }
  | { status: "paywall" };

/** "2028-05-01" to "May 2028", by splitting the string so no time zone can move the month. */
function formatMonth(date: string): string {
  const [year, month] = date.split("-").map(Number);
  return new Date(year, month - 1, 1).toLocaleDateString("en-US", { month: "short", year: "numeric" });
}

const Roadmap = ({ steps }: { steps: RoadmapStep[] }) => (
  <>
    <ol className="roadmap" aria-label="Roadmap">
      {steps.map((step) => (
        <li key={step.label}>
          <span className="roadmap__label">{step.label}</span>
          <span className="roadmap__amount">
            {formatMoney(step.amount)}
            {step.per === "month" ? "/mo" : ""}
            {step.by && <> · by {formatMonth(step.by)}</>}
          </span>
        </li>
      ))}
    </ol>
    {steps.some((step) => step.by) && (
      <span className="roadmap__note">Dates assume your current monthly average holds.</span>
    )}
  </>
);

function sourceLabel(result: AiTipsResponse): string {
  if (result.source === "template") return "Standard wording";
  const standard = result.tips.filter((tip) => tip.source === "template").length;
  return standard === 0
    ? "Worded by AI · numbers checked"
    : `Worded by AI · numbers checked · ${standard} in standard wording`;
}

/**
 * AI recommendations. Nothing is sent until the user presses the button; that press is the consent to send the
 * payload shown on the privacy page. Rules choose each tip from the user's own figures, the AI rewords them, and
 * every number it writes is checked, with standard wording as the fallback.
 */
/** Sends the browser to a Stripe-hosted page. Errors stay on this page. */
const useStripeRedirect = (getUrl: () => Promise<string>) => {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const go = async () => {
    setBusy(true);
    setError("");
    try {
      window.location.assign(await getUrl());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong. Try again.");
      setBusy(false);
    }
  };

  return { busy, error, go };
};

/**
 * Shown when the API answers 402 because the free trial has ended. The API enforces the gate; this card only
 * explains it and opens Stripe Checkout (test mode).
 */
const Paywall = () => {
  const checkout = useStripeRedirect(startCheckout);

  return (
    <div className="border rounded-3 p-3" role="region" aria-label="Upgrade">
      <h5 className="mb-1">Your free trial has ended</h5>
      <p className="card-section-sub mb-3">
        Upgrade for <strong>$2.99/month</strong> to keep getting AI recommendations. Your accounts, transactions,
        charts and health score stay available either way. Cancel any time.
      </p>
      <button
        type="button"
        className="btn btn-primary text-nav"
        onClick={() => void checkout.go()}
        disabled={checkout.busy}
      >
        {checkout.busy && <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" />}
        Upgrade with Stripe
      </button>
      <p className="small text-body-secondary mt-2 mb-0">Test mode: no real card is charged.</p>
      {checkout.error && (
        <p className="text-danger small mt-2 mb-0" role="alert">
          {checkout.error}
        </p>
      )}
    </div>
  );
};

const Insights = () => {
  const [state, setState] = useState<State>({ status: "idle" });
  const [subscribed, setSubscribed] = useState(false);
  const [searchParams] = useSearchParams();
  const checkoutResult = searchParams.get("checkout");
  const portal = useStripeRedirect(openBillingPortal);

  useEffect(() => {
    fetchCurrentUser()
      .then((me) => setSubscribed(me.subscribed))
      .catch(() => setSubscribed(false));
  }, []);

  const generate = async () => {
    setState({ status: "loading" });
    try {
      setState({ status: "ready", result: await generateTips() });
    } catch (err) {
      if (err instanceof ApiError && err.status === 402) {
        setState({ status: "paywall" });
        return;
      }
      setState({
        status: "error",
        message: err instanceof ApiError ? err.message : "Could not generate recommendations. Try again.",
      });
    }
  };

  return (
    <>
      <div className="card glass-card dashboard-card mb-4">
        <div className="card-body d-flex flex-wrap align-items-center justify-content-between gap-3">
          <div>
            <h5 className="mb-1">Your data stays private</h5>
            <p className="card-section-sub mb-0">
              Before any AI analysis, see exactly what would be sent and what stays behind.
            </p>
          </div>
          <Link className="btn btn-primary btn-sm text-nav" to="/privacy">
            See what the AI will receive →
          </Link>
        </div>
      </div>

      {checkoutResult === "success" && !subscribed && (
        <div className="alert alert-success" role="status">
          Payment received. Your subscription activates as soon as Stripe confirms it; refresh in a moment.
        </div>
      )}
      {checkoutResult === "cancelled" && (
        <div className="alert alert-secondary" role="status">
          Checkout cancelled. You have not been charged.
        </div>
      )}

      <div className="card glass-card dashboard-card">
        <div className="card-body">
          <div className="d-flex flex-wrap align-items-start justify-content-between gap-3 mb-3">
            <div>
              <h4 className="mb-1">AI Recommendations</h4>
              <p className="card-section-sub mb-0">
                Monthly steps sized from your own income and spending. Pressing the button sends only the anonymised
                figures shown on the privacy page.
              </p>
            </div>
            <button
              type="button"
              className="btn btn-primary text-nav"
              onClick={() => void generate()}
              disabled={state.status === "loading"}
            >
              {state.status === "loading" && (
                <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" />
              )}
              {state.status === "ready" ? "Refresh recommendations" : "Get AI recommendations"}
            </button>
          </div>

          {subscribed && (
            <div className="d-flex flex-wrap align-items-center gap-2 mb-3">
              <span className="badge text-bg-success">Subscribed</span>
              <button
                type="button"
                className="btn btn-link btn-sm p-0"
                onClick={() => void portal.go()}
                disabled={portal.busy}
              >
                Manage subscription
              </button>
              {portal.error && (
                <span className="text-danger small" role="alert">
                  {portal.error}
                </span>
              )}
            </div>
          )}

          {state.status === "idle" && (
            <p className="card-section-sub mb-0">No recommendations yet. Press the button to get yours.</p>
          )}

          {state.status === "loading" && (
            <p className="card-section-sub mb-0" role="status">
              Analysing your figures…
            </p>
          )}

          {state.status === "paywall" && <Paywall />}

          {state.status === "error" && (
            <p className="text-danger mb-0" role="alert">
              {state.message}
            </p>
          )}

          {state.status === "ready" && (
            <>
              {state.result.tips.length === 0 ? (
                <p className="card-section-sub mb-0">Not enough data for recommendations yet.</p>
              ) : (
                <ul className="recommendation-list">
                  {state.result.tips.map((tip) => (
                    <li key={tip.id}>
                      <span className="recommendation-title">{tip.title}</span>
                      <span className="recommendation-text">{tip.text}</span>
                      {tip.steps.length > 0 && <Roadmap steps={tip.steps} />}
                    </li>
                  ))}
                </ul>
              )}
              <ul className="recommendation-notes">
                {state.result.dataGaps.map((gap) => (
                  <li key={gap}>{gap}</li>
                ))}
              </ul>
              <p className="recommendation-footer">
                <span className="recommendation-source">{sourceLabel(state.result)}</span>
                General information, not financial advice.
              </p>
            </>
          )}
        </div>
      </div>
    </>
  );
};

export default Insights;
