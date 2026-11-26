import { useEffect, useState } from "react";
import { ApiError } from "../api/auth";
import { fetchRecurring, formatMoney, toDateString, type RecurringList, type RecurringStream } from "../api/transactions";

type State = { status: "loading" } | { status: "ready"; list: RecurringList } | { status: "error"; message: string };

const CADENCE_LABELS: Record<RecurringStream["cadence"], string> = {
  weekly: "Weekly",
  biweekly: "Every 2 weeks",
  monthly: "Monthly",
  quarterly: "Quarterly",
  yearly: "Yearly",
};

/** yyyy-MM-dd to "Dec 10, 2026", built from parts so no time zone can move the day. */
function formatDue(date: string): string {
  const [year, month, day] = date.split("-").map(Number);
  return new Date(year, month - 1, day).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

/**
 * Repeating charges detected from the ledger, soonest first. Detection runs on the server on every
 * request; nothing here is stored, so bills cannot be confirmed or dismissed yet.
 */
const Bills = () => {
  const [state, setState] = useState<State>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setState({ status: "loading" });
      try {
        const list = await fetchRecurring();
        if (!cancelled) setState({ status: "ready", list });
      } catch (err) {
        if (!cancelled) {
          setState({
            status: "error",
            message: err instanceof ApiError ? err.message : "Could not load your bills. Try again.",
          });
        }
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  const today = toDateString(new Date());

  return (
    <div className="card glass-card dashboard-card">
      <div className="card-body">
        <div className="bills-header mb-1">
          <div>
            <h4 className="mb-0">Upcoming Bills</h4>
            <p className="card-section-sub mb-0">
              Repeating charges found in your last 13 months
              {state.status === "ready" && state.list.streams.length > 0 && (
                <> · about {formatMoney(state.list.monthlyTotal)} a month</>
              )}
            </p>
          </div>
        </div>

        {state.status === "loading" && (
          <div className="bills-state" role="status">
            <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" /> Loading…
          </div>
        )}

        {state.status === "error" && (
          <div className="bills-state" role="alert">
            {state.message}
            <button type="button" className="btn btn-sm btn-outline-dark ms-3" onClick={() => setAttempt((n) => n + 1)}>
              Retry
            </button>
          </div>
        )}

        {state.status === "ready" && state.list.streams.length === 0 && (
          <div className="bills-state">
            No repeating charges yet. A bill shows up here after it has been charged three times.
          </div>
        )}

        {state.status === "ready" && state.list.streams.length > 0 && (
          <div className="bill-list mt-3">
            {state.list.streams.map((stream) => (
              <div className="bill-item" key={`${stream.merchant}|${stream.nextDate}`}>
                <div
                  className={`bill-icon ${stream.isSubscription ? "bill-icon--subscription" : "bill-icon--bill"}`}
                  aria-hidden="true"
                >
                  {stream.merchant.trim().charAt(0).toUpperCase() || "?"}
                </div>
                <div className="bill-info">
                  <div className="bill-name">
                    {stream.merchant}
                    {stream.isSubscription && <span className="bill-badge">Subscription</span>}
                  </div>
                  <div className="bill-due">
                    {CADENCE_LABELS[stream.cadence]} · {stream.nextDate < today ? "Expected" : "Due"} {formatDue(stream.nextDate)}
                  </div>
                </div>
                <div className="bill-amount">
                  {formatMoney(stream.typicalAmount)}
                  {stream.lastAmount !== stream.typicalAmount && (
                    <span className="bill-amount__sub">last {formatMoney(stream.lastAmount)}</span>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};

export default Bills;
