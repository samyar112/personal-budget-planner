import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/auth";
import { fetchAllTransactions, formatMoney, summarize, toDateString, type LedgerSummary } from "../api/transactions";
import ConnectBankButton from "../components/ConnectBankButton";

const Home = () => {
  const [summary, setSummary] = useState<
    { status: "loading" } | { status: "error"; message: string } | { status: "ready"; summary: LedgerSummary }
  >({ status: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setSummary({ status: "loading" });
      try {
        const today = new Date();
        const windowStart = new Date(today);
        windowStart.setDate(today.getDate() - 29);
        const rows = await fetchAllTransactions({ from: toDateString(windowStart), to: toDateString(today) });
        if (!cancelled) setSummary({ status: "ready", summary: summarize(rows) });
      } catch (err) {
        if (!cancelled) {
          setSummary({
            status: "error",
            message: err instanceof ApiError ? err.message : "Could not load transactions. Try again.",
          });
        }
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  return (
    <>
      {/* Hero */}
      <section className="hero-section">
        <div className="hero-section__inner glass-panel">
          <h2 className="text-headline text-center">
            Smart Budgeting.{" "}
            <span className="text-primary">Private by Design.</span>
          </h2>

          <p className="text-body-brand mt-3 text-center">
            Connect your bank or upload a statement, and get budget
            recommendations built from your own spending. Identifying details
            are stripped out before any AI sees your data.
          </p>

          <div className="d-flex flex-wrap justify-content-center align-items-center gap-3 mt-4">
            <ConnectBankButton onLinked={() => setAttempt((current) => current + 1)} />
            <button type="button" className="btn btn-primary text-nav">
              Upload Statement
            </button>
          </div>
        </div>
      </section>

      {/* Privacy Banner */}
      <div className="privacy-banner mb-4">
        <span className="me-2">🔒</span>
        All personal information is anonymized before AI analysis.
      </div>

      {summary.status === "error" && (
        <div className="alert alert-warning d-flex align-items-center gap-2" role="alert">
          <span>{summary.message}</span>
          <button
            type="button"
            className="btn btn-sm btn-outline-dark ms-auto"
            onClick={() => setAttempt((current) => current + 1)}
          >
            Try again
          </button>
        </div>
      )}

      {/* Summary Cards */}
      <div className="row g-4 mb-4">
        {(
          [
            ["income", "Income", summary.status === "ready" ? summary.summary.income : null],
            ["expense", "Expenses", summary.status === "ready" ? summary.summary.expenses : null],
            ["savings", "Savings", summary.status === "ready" ? summary.summary.savings : null],
          ] as const
        ).map(([className, label, value]) => (
          <div className="col-md-4" key={className}>
            <div className={`summary-card ${className}`}>
              <h6>{label} <span className="summary-card__window">· last 30 days</span></h6>
              <h3>{value === null ? "—" : formatMoney(value)}</h3>
            </div>
          </div>
        ))}
      </div>

      <Link className="btn btn-secondary btn-sm text-nav" to="/transactions">
        View transactions →
      </Link>
    </>
  );
};

export default Home;
