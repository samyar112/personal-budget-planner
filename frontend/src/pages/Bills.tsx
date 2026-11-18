/** Placeholder: hard-coded sample bills until recurring detection (#37). */
const Bills = () => (
  <div className="card glass-card dashboard-card">
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
        <div className="bill-item">
          <div className="bill-icon bill-icon--netflix">N</div>
          <div className="bill-info">
            <div className="bill-name">Netflix</div>
            <div className="bill-due">Due Aug 10, 2025</div>
          </div>
          <div className="bill-amount">$13.99</div>
        </div>

        <div className="bill-item">
          <div className="bill-icon bill-icon--apple">&#63743;</div>
          <div className="bill-info">
            <div className="bill-name">Apple One</div>
            <div className="bill-due">Due Aug 14, 2025</div>
          </div>
          <div className="bill-amount">$19.95</div>
        </div>

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
);

export default Bills;
