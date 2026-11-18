import { useEffect, useState } from "react";
import { ApiError } from "../api/auth";
import { fetchTransactions, formatDate, formatMoney, type Transaction } from "../api/transactions";

/** Newest rows first. One page for now; paging and filters come with categorisation (#29). */
const PAGE_SIZE = 50;

const Transactions = () => {
  const [ledger, setLedger] = useState<
    { status: "loading" } | { status: "error"; message: string } | { status: "ready"; rows: Transaction[] }
  >({ status: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setLedger({ status: "loading" });
      try {
        const page = await fetchTransactions({ page: 1, pageSize: PAGE_SIZE });
        if (!cancelled) setLedger({ status: "ready", rows: page.items });
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
  }, [attempt]);

  return (
    <div className="card glass-card dashboard-card">
      <div className="card-body">
        <h4 className="mb-3">Recent Transactions</h4>

        {ledger.status === "loading" && (
          <p className="text-body-secondary mb-0" role="status">Loading transactions…</p>
        )}

        {ledger.status === "error" && (
          <div className="alert alert-warning d-flex align-items-center gap-2 mb-0" role="alert">
            <span>{ledger.message}</span>
            <button
              type="button"
              className="btn btn-sm btn-outline-dark ms-auto"
              onClick={() => setAttempt((current) => current + 1)}
            >
              Try again
            </button>
          </div>
        )}

        {ledger.status === "ready" && ledger.rows.length === 0 && (
          <p className="text-body-secondary mb-0">
            No transactions yet. Connect a bank or upload a statement to see them here.
          </p>
        )}

        {ledger.status === "ready" && ledger.rows.length > 0 && (
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
                {ledger.rows.map((row) => (
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
  );
};

export default Transactions;
