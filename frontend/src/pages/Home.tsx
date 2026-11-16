import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ApiError, fetchCurrentUser, logoutUser, resendVerification } from "../api/auth";
import {
  fetchAllTransactions,
  fetchTransactions,
  formatDate,
  formatMoney,
  summarize,
  toDateString,
  type LedgerSummary,
  type Transaction,
} from "../api/transactions";
import BrandName, { BRAND_NAME, clearAuthSession } from "../components/BrandName";
import AmbientBackground from "../components/AmbientBackground";
import "./Home.css";

const Home = () => {
  const navigate = useNavigate();
  const [unverifiedEmail, setUnverifiedEmail] = useState<string | null>(null);
  const [resendStatus, setResendStatus] = useState("");
  const [isAdmin, setIsAdmin] = useState(false);
  const [ledger, setLedger] = useState<
    | { status: "loading" }
    | { status: "error"; message: string }
    | { status: "ready"; recent: Transaction[]; summary: LedgerSummary; hasRows: boolean }
  >({ status: "loading" });
  const [ledgerAttempt, setLedgerAttempt] = useState(0);

  useEffect(() => {
    // The Google stub session has no API user; nothing to prompt for then.
    fetchCurrentUser()
      .then((me) => {
        setUnverifiedEmail(me.emailVerified ? null : me.email);
        setIsAdmin(me.role === "Admin");
      })
      .catch(() => setUnverifiedEmail(null));
  }, []);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setLedger({ status: "loading" });
      try {
        const today = new Date();
        const windowStart = new Date(today);
        windowStart.setDate(today.getDate() - 29);
        const [recent, windowRows] = await Promise.all([
          fetchTransactions({ page: 1, pageSize: 10 }),
          fetchAllTransactions({ from: toDateString(windowStart), to: toDateString(today) }),
        ]);
        if (!cancelled) {
          setLedger({
            status: "ready",
            recent: recent.items,
            summary: summarize(windowRows),
            hasRows: recent.total > 0,
          });
        }
      } catch (err) {
        if (!cancelled) {
          setLedger({
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
  }, [ledgerAttempt]);

  const handleResend = async () => {
    if (!unverifiedEmail) return;
    setResendStatus("");
    try {
      await resendVerification(unverifiedEmail);
      setResendStatus("Verification email sent. Check your inbox.");
    } catch (err) {
      setResendStatus(err instanceof ApiError ? err.message : "Could not send the email. Try again.");
    }
  };

  const handleLogout = async () => {
    try {
      await logoutUser();
    } catch {
      // Still clear client state if the network call fails.
    }
    clearAuthSession();
    navigate("/");
  };

  return (
    <div className="home-page">
      <AmbientBackground />
      <nav className="navbar navbar-expand-lg px-4 py-3 finance-navbar">
        <div className="container-fluid">
          <Link className="navbar-brand fw-bold" to="/home">
            <BrandName logoSize="sm" />
          </Link>

          <button
            className="navbar-toggler"
            type="button"
            data-bs-toggle="collapse"
            data-bs-target="#nav"
          >
            <span className="navbar-toggler-icon"></span>
          </button>

          <div className="collapse navbar-collapse" id="nav">
            <ul className="navbar-nav ms-auto gap-3">
              <li className="nav-item">
                <a className="nav-link" href="#">
                  Dashboard
                </a>
              </li>

              <li className="nav-item">
                <a className="nav-link" href="#">
                  Upload
                </a>
              </li>

              <li className="nav-item">
                <a className="nav-link" href="#">
                  Insights
                </a>
              </li>

              {isAdmin && (
                <li className="nav-item">
                  <Link className="nav-link" to="/admin/users">
                    Admin
                  </Link>
                </li>
              )}

              <li className="nav-item">
                <button
                  type="button"
                  className="btn btn-secondary btn-sm text-nav"
                  onClick={handleLogout}
                >
                  Log out
                </button>
              </li>
            </ul>
          </div>
        </div>
      </nav>

      {/* Hero */}
      <section className="hero-section">
        <div className="container hero-section__inner glass-panel">
          <h1 className="text-headline text-center">
            Smart Budgeting.{" "}
            <span className="text-primary">Private by Design.</span>
          </h1>

          <p className="text-body-brand mt-3 text-center">
            Upload your financial statements and receive personalized budget
            recommendations while keeping your sensitive information protected.
          </p>

          <button type="button" className="btn btn-primary text-nav mt-3 d-block mx-auto">
            Upload Statement
          </button>
        </div>
      </section>

      <div className="container py-5">
        {unverifiedEmail && (
          <div className="alert alert-warning d-flex flex-wrap align-items-center gap-2" role="status">
            <span>
              Verify <strong>{unverifiedEmail}</strong> to unlock your finances. Check your inbox for the link.
            </span>
            <button type="button" className="btn btn-sm btn-outline-dark ms-auto" onClick={handleResend}>
              Resend email
            </button>
            {resendStatus && <span className="w-100 small">{resendStatus}</span>}
          </div>
        )}

        {/* Privacy Banner */}
        <div className="privacy-banner mb-4">
          <span className="me-2">🔒</span>
          All personal information is anonymized before AI analysis.
        </div>

        {/* Summary Cards */}
        <div className="row g-4 mb-4">
          {(
            [
              ["income", "Income", ledger.status === "ready" ? ledger.summary.income : null],
              ["expense", "Expenses", ledger.status === "ready" ? ledger.summary.expenses : null],
              ["savings", "Savings", ledger.status === "ready" ? ledger.summary.savings : null],
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

        {/* Main Content */}
        <div className="row g-4">
          {/* Spending Breakdown */}
          <div className="col-lg-7">
            <div className="card glass-card dashboard-card">
              <div className="card-body">
                <h4 className="mb-4">Spending Breakdown</h4>

                <div className="placeholder-chart">Chart Placeholder</div>
              </div>
            </div>
          </div>

          {/* Recommendations */}
          <div className="col-lg-5">
            <div className="card glass-card dashboard-card h-100">
              <div className="card-body">
                <h4 className="mb-4">AI Recommendations</h4>

                <ul className="recommendation-list">
                  <li>Reduce dining expenses by 15%</li>
                  <li>Increase monthly savings by $200</li>
                  <li>Review recurring subscriptions</li>
                  <li>Build a 6-month emergency fund</li>
                </ul>
              </div>
            </div>
          </div>
        </div>

        {/* Transactions */}
        <div className="card glass-card dashboard-card mt-4">
        <div className="card-body">
            <div className="d-flex justify-content-between align-items-center mb-3">
            <h4>Recent Transactions</h4>
            <button type="button" className="btn btn-primary btn-sm text-nav">
              View All
            </button>
            </div>

            {ledger.status === "loading" && (
              <p className="text-body-secondary mb-0" role="status">Loading transactions…</p>
            )}

            {ledger.status === "error" && (
              <div className="alert alert-warning d-flex align-items-center gap-2 mb-0" role="alert">
                <span>{ledger.message}</span>
                <button
                  type="button"
                  className="btn btn-sm btn-outline-dark ms-auto"
                  onClick={() => setLedgerAttempt((attempt) => attempt + 1)}
                >
                  Try again
                </button>
              </div>
            )}

            {ledger.status === "ready" && !ledger.hasRows && (
              <p className="text-body-secondary mb-0">
                No transactions yet. Connect a bank or upload a statement to see them here.
              </p>
            )}

            {ledger.status === "ready" && ledger.hasRows && (
              <div className="table-responsive">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Description</th>
                      <th>Category</th>
                      <th className="text-end">Amount</th>
                    </tr>
                  </thead>

                  <tbody>
                    {ledger.recent.map((row) => (
                      <tr key={row.id}>
                        <td>{formatDate(row.date)}</td>
                        <td>{row.description}</td>
                        <td>{row.category ?? "Uncategorised"}</td>
                        <td className={`text-end ${row.amount < 0 ? "amount-negative" : "amount-positive"}`}>
                          {row.amount < 0 ? "-" : "+"}
                          {formatMoney(Math.abs(row.amount))}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
        </div>
        </div>

        {/* Card Wallet & Upcoming Bills Row */}
        <div className="row g-4 mt-2">
          {/* Your Card */}
          <div className="col-lg-6">
            <div className="card glass-card dashboard-card">
              <div className="card-body">
                <h4 className="mb-1">Your Card</h4>
                <p className="card-section-sub mb-3">Stay ahead of your payments</p>

                {/* Payment Cards Stack */}
                <div className="payment-cards-stack mb-3">
                  <div className="payment-card payment-card--navy">
                    <div className="payment-card__left">
                      <div className="mastercard-logo">
                        <span className="mc-dot mc-dot--red"></span>
                        <span className="mc-dot mc-dot--yellow"></span>
                      </div>
                      <div className="payment-card__name">Franklin Mgmt Inc</div>
                      <div className="payment-card__num">**** **** **** 207</div>
                    </div>
                    <div className="payment-card__right">
                      <div className="card-chip"></div>
                      <div className="card-contactless">&#x1F6DC;</div>
                    </div>
                  </div>

                  <div className="payment-card payment-card--orange">
                    <div className="payment-card__left">
                      <div className="mastercard-logo">
                        <span className="mc-dot mc-dot--red"></span>
                        <span className="mc-dot mc-dot--yellow"></span>
                      </div>
                      <div className="payment-card__name">Valiant Capital Mgmt LP</div>
                      <div className="payment-card__num">**** **** **** 202</div>
                    </div>
                    <div className="payment-card__right">
                      <div className="card-chip"></div>
                      <div className="card-contactless">&#x1F6DC;</div>
                    </div>
                  </div>

                  <div className="payment-card payment-card--green">
                    <div className="payment-card__left">
                      <div className="mastercard-logo">
                        <span className="mc-dot mc-dot--red"></span>
                        <span className="mc-dot mc-dot--yellow"></span>
                      </div>
                      <div className="payment-card__name">Valiant Capital Mgmt LP</div>
                      <div className="payment-card__num">**** **** **** 207</div>
                    </div>
                    <div className="payment-card__right">
                      <div className="card-chip"></div>
                      <div className="card-contactless">&#x1F6DC;</div>
                    </div>
                  </div>
                </div>

                {/* Add Card Button */}
                <button type="button" className="btn btn-secondary w-100 text-nav border-dashed">
                  <span className="add-card-btn__icon">+</span>
                  Add Card
                </button>
              </div>
            </div>
          </div>

          {/* Upcoming Bills */}
          <div className="col-lg-6">
            <div className="card glass-card dashboard-card h-100">
              <div className="card-body">
                <div className="bills-header mb-1">
                  <div>
                    <h4 className="mb-0">Upcoming Bills</h4>
                    <p className="card-section-sub mb-0">Stay ahead of your payments</p>
                  </div>
                  <button type="button" className="btn btn-secondary btn-sm text-nav">
                    🔔 Reminders
                  </button>
                </div>

                <div className="bill-list mt-3">
                  {/* Netflix */}
                  <div className="bill-item">
                    <div className="bill-icon bill-icon--netflix">N</div>
                    <div className="bill-info">
                      <div className="bill-name">Netflix</div>
                      <div className="bill-due">Due Aug 10, 2025</div>
                    </div>
                    <div className="bill-amount">$13.99</div>
                  </div>

                  {/* Apple One */}
                  <div className="bill-item">
                    <div className="bill-icon bill-icon--apple">&#63743;</div>
                    <div className="bill-info">
                      <div className="bill-name">Apple One</div>
                      <div className="bill-due">Due Aug 14, 2025</div>
                    </div>
                    <div className="bill-amount">$19.95</div>
                  </div>

                  {/* Adobe Creative Cloud */}
                  <div className="bill-item">
                    <div className="bill-icon bill-icon--adobe">Ai</div>
                    <div className="bill-info">
                      <div className="bill-name">Adobe Creative Cloud</div>
                      <div className="bill-due">Due Aug 18, 2025</div>
                    </div>
                    <div className="bill-amount">$52.99</div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <footer className="finance-footer glass-panel glass-panel--footer mt-5">
          <div className="container py-4">
            <div className="row">
              <div className="col-md-4">
                <h5 className="fw-bold">
                  <BrandName logoSize="md" />
                </h5>
                <p className="text-body-secondary">
                  Private, AI-powered budgeting that keeps your financial data
                  secure.
                </p>
              </div>

              <div className="col-md-4">
                <h6>Product</h6>
                <ul className="footer-links">
                  <li>Dashboard</li>
                  <li>Upload Statements</li>
                  <li>Insights</li>
                </ul>
              </div>

              <div className="col-md-4">
                <h6>Company</h6>
                <ul className="footer-links">
                  <li>About</li>
                  <li>Privacy</li>
                  <li>Security</li>
                </ul>
              </div>
            </div>

            <hr />

            <p className="text-center text-body-secondary mb-0">
              © {new Date().getFullYear()} {BRAND_NAME}. All rights reserved.
            </p>
          </div>
        </footer>
      </div>
    </div>
  );
};

export default Home;
