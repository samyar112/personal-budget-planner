import { Link } from "react-router-dom";

/** Placeholder: static sample tips until AI recommendations are built. */
const Insights = () => (
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
        <h4 className="mb-4">AI Recommendations</h4>
        <ul className="recommendation-list">
          <li>Reduce dining expenses by 15%</li>
          <li>Increase monthly savings by $200</li>
          <li>Review recurring subscriptions</li>
          <li>Build a 6-month emergency fund</li>
        </ul>
      </div>
    </div>
  </>
);

export default Insights;
