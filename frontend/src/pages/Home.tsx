import "./Home.css";

const Home = () => {
  return (
    <div className="home-page">
      <nav className="navbar navbar-expand-lg navbar-dark px-4 py-3 finance-navbar">
        <div className="container-fluid">
          <a className="navbar-brand fw-bold" href="#">
            FinGuard
          </a>

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

              <li className="nav-item">
                <button className="btn btn-light btn-sm px-3">Login</button>
              </li>
            </ul>
          </div>
        </div>
      </nav>

      {/* Hero */}
      <section className="hero-section">
        <div className="container text-center">
          <h1 className="display-4 fw-bold">
            Smart Budgeting. Private by Design.
          </h1>

          <p className="lead mt-3">
            Upload your financial statements and receive personalized budget
            recommendations while keeping your sensitive information protected.
          </p>

          <button className="btn btn-light btn-lg mt-3">
            Upload Statement
          </button>
        </div>
      </section>

      <div className="container py-5">
        {/* Privacy Banner */}
        <div className="privacy-banner mb-4">
          <span className="me-2">🔒</span>
          All personal information is anonymized before AI analysis.
        </div>

        {/* Summary Cards */}
        <div className="row g-4 mb-4">
          <div className="col-md-3">
            <div className="summary-card income">
              <h6>Income</h6>
              <h3>$4,500</h3>
            </div>
          </div>

          <div className="col-md-3">
            <div className="summary-card expense">
              <h6>Expenses</h6>
              <h3>$3,250</h3>
            </div>
          </div>

          <div className="col-md-3">
            <div className="summary-card savings">
              <h6>Savings</h6>
              <h3>$1,250</h3>
            </div>
          </div>

            <div className="col-md-3">
                <div className="summary-card score">
                    <h6>Financial Score</h6>
                    <h3>84<span className="score-max">/100</span></h3>
                    <span className={`score-badge ${84 >= 80 ? "badge-good" : 84 >= 60 ? "badge-fair" : "badge-poor"}`}>
                    {84 >= 80 ? "✓ Good" : 84 >= 60 ? "⚠ Fair" : "✗ Needs Work"}
                    </span>
                    <p className="score-label">Based on savings, spending & debt</p>
                </div>
            </div>
        </div>
        
        {/* Main Content */}
        <div className="row g-4">
          {/* Spending Breakdown */}
          <div className="col-lg-7">
            <div className="card dashboard-card">
              <div className="card-body">
                <h4 className="mb-4">Spending Breakdown</h4>

                <div className="placeholder-chart">Chart Placeholder</div>
              </div>
            </div>
          </div>

          {/* Recommendations */}
          <div className="col-lg-5">
            <div className="card dashboard-card h-100">
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
        <div className="card dashboard-card mt-4">
        <div className="card-body">
            <div className="d-flex justify-content-between align-items-center mb-3">
            <h4>Recent Transactions</h4>
            <button className="btn btn-primary">View All</button>
            </div>

            <div className="table-responsive">
            <table className="table">
                <thead>
                <tr>
                    <th>Date</th>
                    <th>Description</th>
                    <th>Category</th>
                    <th>Card</th>
                    <th className="text-end">Amount</th>
                </tr>
                </thead>

                <tbody>
                <tr>
                    <td>06/20/2026</td>
                    <td>Walmart</td>
                    <td>Groceries</td>
                    <td>
                    <div className="card-detail">
                        <span className="card-bank">Franklin Mgmt Inc</span>
                        <span className="card-ending">•••• 207</span>
                    </div>
                    </td>
                    <td className="text-danger text-end">-$54.82</td>
                </tr>

                <tr>
                    <td>06/18/2026</td>
                    <td>Payroll Deposit</td>
                    <td>Income</td>
                    <td>
                    <div className="card-detail">
                        <span className="card-bank">Valiant Capital Mgmt LP</span>
                        <span className="card-ending">•••• 202</span>
                    </div>
                    </td>
                    <td className="text-success text-end">+$2,250.00</td>
                </tr>

                <tr>
                    <td>06/17/2026</td>
                    <td>Netflix</td>
                    <td>Entertainment</td>
                    <td>
                    <div className="card-detail">
                        <span className="card-bank">Valiant Capital Mgmt LP</span>
                        <span className="card-ending">•••• 207</span>
                    </div>
                    </td>
                    <td className="text-danger text-end">-$15.99</td>
                </tr>
                </tbody>
            </table>
            </div>
        </div>
        </div>

        {/* Card Wallet & Upcoming Bills Row */}
        <div className="row g-4 mt-2">
          {/* Your Card */}
          <div className="col-lg-6">
            <div className="card dashboard-card">
              <div className="card-body">
                <h4 className="mb-1">Your Card</h4>
                <p className="card-section-sub mb-3">Stay ahead of your payments</p>

                {/* Payment Cards Stack */}
                <div className="payment-cards-stack mb-3">
                  <div className="payment-card payment-card--purple">
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

                  <div className="payment-card payment-card--dark">
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
                <button className="add-card-btn">
                  <span className="add-card-btn__icon">+</span>
                  Add Card
                </button>
              </div>
            </div>
          </div>

          {/* Upcoming Bills */}
          <div className="col-lg-6">
            <div className="card dashboard-card h-100">
              <div className="card-body">
                <div className="bills-header mb-1">
                  <div>
                    <h4 className="mb-0">Upcoming Bills</h4>
                    <p className="card-section-sub mb-0">Stay ahead of your payments</p>
                  </div>
                  <button className="reminders-btn">
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

        <footer className="finance-footer mt-5">
          <div className="container py-4">
            <div className="row">
              <div className="col-md-4">
                <h5 className="fw-bold">FinGuard</h5>
                <p className="text-muted">
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

            <p className="text-center text-muted mb-0">
              © {new Date().getFullYear()} FinGuard. All rights reserved.
            </p>
          </div>
        </footer>
      </div>
    </div>
  );
};

export default Home;
