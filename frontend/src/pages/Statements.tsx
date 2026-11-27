import { useEffect, useRef, useState, type FormEvent } from "react";
import { ApiError } from "../api/auth";
import {
  fetchStatement,
  fetchStatementAccounts,
  uploadStatement,
  type StatementAccount,
  type StatementDetail,
} from "../api/statements";
import { formatMoney } from "../api/transactions";

type ListState =
  | { status: "loading" }
  | { status: "ready"; accounts: StatementAccount[] }
  | { status: "error"; message: string };

type DetailState =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "ready"; detail: StatementDetail }
  | { status: "error"; message: string };

/** yyyy-MM-dd to "Oct 3, 2026", built from parts so no time zone can move the day. */
function formatDate(date: string): string {
  const [year, month, day] = date.split("-").map(Number);
  return new Date(year, month - 1, day).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

/**
 * Uploaded PDF statements, kept apart from the main ledger views: nothing here counts toward the
 * dashboard, charts, bills, health score or AI tips.
 */
const Statements = () => {
  const [list, setList] = useState<ListState>({ status: "loading" });
  const [selected, setSelected] = useState<string | null>(null);
  const [detail, setDetail] = useState<DetailState>({ status: "idle" });
  const [refresh, setRefresh] = useState(0);
  const [uploading, setUploading] = useState(false);
  const [notice, setNotice] = useState<{ kind: "success" | "danger"; text: string } | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setList({ status: "loading" });
      try {
        const accounts = await fetchStatementAccounts();
        if (cancelled) return;
        setList({ status: "ready", accounts });
        setSelected((current) => current ?? accounts[0]?.id ?? null);
      } catch (err) {
        if (!cancelled) {
          setList({
            status: "error",
            message: err instanceof ApiError ? err.message : "Could not load your statements. Try again.",
          });
        }
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [refresh]);

  useEffect(() => {
    // No selection only happens with no statements, and then the detail is not rendered.
    if (!selected) return;

    let cancelled = false;
    const load = async () => {
      setDetail({ status: "loading" });
      try {
        const result = await fetchStatement(selected);
        if (!cancelled) setDetail({ status: "ready", detail: result });
      } catch (err) {
        if (!cancelled) {
          setDetail({
            status: "error",
            message: err instanceof ApiError ? err.message : "Could not load this statement. Try again.",
          });
        }
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [selected, refresh]);

  const onUpload = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const file = fileInput.current?.files?.[0];
    if (!file) {
      setNotice({ kind: "danger", text: "Choose a PDF statement first." });
      return;
    }

    setUploading(true);
    setNotice(null);
    try {
      const result = await uploadStatement(file);
      const skipped = result.skipped > 0 ? ` ${result.skipped} already imported.` : "";
      setNotice({ kind: "success", text: `Imported ${result.imported} transactions.${skipped}` });
      setSelected(result.accountId);
      setRefresh((n) => n + 1);
      if (fileInput.current) fileInput.current.value = "";
    } catch (err) {
      setNotice({
        kind: "danger",
        text: err instanceof ApiError ? err.message : "Could not upload the statement. Try again.",
      });
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="card glass-card dashboard-card">
      <div className="card-body">
        <h4 className="mb-0">Statements</h4>
        <p className="card-section-sub">
          PDF statements are shown here only. They do not count toward your dashboard, bills, health score or AI
          tips. The file is read once and not stored.
        </p>

        <form className="d-flex flex-wrap gap-2 align-items-center mb-3" onSubmit={onUpload}>
          <input
            ref={fileInput}
            type="file"
            accept="application/pdf,.pdf"
            className="form-control"
            style={{ maxWidth: 360 }}
            aria-label="PDF statement"
            disabled={uploading}
          />
          <button type="submit" className="btn btn-dark" disabled={uploading}>
            {uploading && <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" />}
            Upload
          </button>
        </form>

        {notice && (
          <div className={`alert alert-${notice.kind} py-2`} role={notice.kind === "danger" ? "alert" : "status"}>
            {notice.text}
          </div>
        )}

        {list.status === "loading" && (
          <div role="status">
            <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" /> Loading…
          </div>
        )}

        {list.status === "error" && (
          <div role="alert">
            {list.message}
            <button type="button" className="btn btn-sm btn-outline-dark ms-3" onClick={() => setRefresh((n) => n + 1)}>
              Retry
            </button>
          </div>
        )}

        {list.status === "ready" && list.accounts.length === 0 && (
          <p className="text-body-secondary mb-0">No statements yet. Upload a PDF statement to see it here.</p>
        )}

        {list.status === "ready" && list.accounts.length > 0 && (
          <>
            <select
              className="form-select mb-3"
              style={{ maxWidth: 360 }}
              aria-label="Statement account"
              value={selected ?? ""}
              onChange={(event) => setSelected(event.target.value)}
            >
              {list.accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.name} · {account.transactionCount} transactions
                </option>
              ))}
            </select>

            {detail.status === "loading" && (
              <div role="status">
                <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" /> Loading…
              </div>
            )}

            {detail.status === "error" && <div role="alert">{detail.message}</div>}

            {detail.status === "ready" && (
              <>
                <div className="d-flex flex-wrap gap-4 mb-3">
                  <div>
                    <div className="card-section-sub">Income</div>
                    <strong className="amount-positive">{formatMoney(detail.detail.income)}</strong>
                  </div>
                  <div>
                    <div className="card-section-sub">Expenses</div>
                    <strong className="amount-negative">{formatMoney(detail.detail.expenses)}</strong>
                  </div>
                  <div>
                    <div className="card-section-sub">Net</div>
                    <strong>{formatMoney(detail.detail.net)}</strong>
                  </div>
                  {detail.detail.account.firstDate && detail.detail.account.lastDate && (
                    <div>
                      <div className="card-section-sub">Period</div>
                      <strong>
                        {formatDate(detail.detail.account.firstDate)} – {formatDate(detail.detail.account.lastDate)}
                      </strong>
                    </div>
                  )}
                </div>

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
                      {detail.detail.transactions.map((row) => (
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
              </>
            )}
          </>
        )}
      </div>
    </div>
  );
};

export default Statements;
