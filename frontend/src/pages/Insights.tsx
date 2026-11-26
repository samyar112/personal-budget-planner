import { useState } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/auth";
import { generateTips, type AiTipsResponse, type RoadmapStep } from "../api/ai";
import { formatMoney } from "../api/transactions";

type State =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "ready"; result: AiTipsResponse }
  | { status: "error"; message: string };

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
const Insights = () => {
  const [state, setState] = useState<State>({ status: "idle" });

  const generate = async () => {
    setState({ status: "loading" });
    try {
      setState({ status: "ready", result: await generateTips() });
    } catch (err) {
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

          {state.status === "idle" && (
            <p className="card-section-sub mb-0">No recommendations yet. Press the button to get yours.</p>
          )}

          {state.status === "loading" && (
            <p className="card-section-sub mb-0" role="status">
              Analysing your figures…
            </p>
          )}

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
