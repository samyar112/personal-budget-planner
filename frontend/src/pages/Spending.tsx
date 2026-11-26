import { useEffect, useMemo, useState } from "react";
import { fetchPlaidItems, type LinkedAccount } from "../api/plaid";
import { toDateString, type SpendingFilter } from "../api/transactions";
import CategoryChart from "../components/charts/CategoryChart";
import MonthlyTrendChart from "../components/charts/MonthlyTrendChart";
import TopMerchantsChart from "../components/charts/TopMerchantsChart";
import "./Spending.css";

type Period = "thisMonth" | "lastMonth" | "last3Months" | "thisYear";

const PERIODS: { value: Period; label: string }[] = [
  { value: "thisMonth", label: "This month" },
  { value: "lastMonth", label: "Last month" },
  { value: "last3Months", label: "Last 3 months" },
  { value: "thisYear", label: "This year" },
];

/** Calendar ranges in local time, so "this month" starts on the 1st where the user is. */
function periodRange(period: Period, today: Date): { from: string; to: string } {
  const year = today.getFullYear();
  const month = today.getMonth();
  switch (period) {
    case "thisMonth":
      return { from: toDateString(new Date(year, month, 1)), to: toDateString(today) };
    case "lastMonth":
      return { from: toDateString(new Date(year, month - 1, 1)), to: toDateString(new Date(year, month, 0)) };
    case "last3Months":
      return { from: toDateString(new Date(year, month - 2, 1)), to: toDateString(today) };
    case "thisYear":
      return { from: toDateString(new Date(year, 0, 1)), to: toDateString(today) };
  }
}

/** The trend shows six months ending with the period, or the whole year so far for "This year". */
function trendRange(period: Period, to: string, today: Date): { from: string; to: string } {
  if (period === "thisYear") {
    return { from: toDateString(new Date(today.getFullYear(), 0, 1)), to };
  }
  const [year, month] = to.split("-").map(Number);
  return { from: toDateString(new Date(year, month - 6, 1)), to };
}

/**
 * Where the money went (by category), how it is trending (by month) and who it went to (by payee).
 * One filter bar drives all three; clicking a category narrows the merchants chart.
 */
const Spending = () => {
  const [period, setPeriod] = useState<Period>("thisMonth");
  const [accountId, setAccountId] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<{ account: LinkedAccount; institution: string | null }[]>([]);

  useEffect(() => {
    let cancelled = false;
    // The account list only feeds the filter; if it fails, "All accounts" still works.
    fetchPlaidItems()
      .then((items) => {
        if (!cancelled) {
          setAccounts(items.flatMap((item) => item.accounts.map((account) => ({ account, institution: item.institution }))));
        }
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

  const { filter, trendFilter } = useMemo(() => {
    const today = new Date();
    const range = periodRange(period, today);
    const account = accountId || undefined;
    return {
      filter: { ...range, accountId: account } satisfies SpendingFilter,
      trendFilter: { ...trendRange(period, range.to, today), accountId: account } satisfies SpendingFilter,
    };
  }, [period, accountId]);

  return (
    <div className="spending-page">
      <div className="card glass-card dashboard-card mb-4">
        <div className="card-body spending-filters">
          <h4 className="mb-0 me-auto">Spending</h4>
          <div className="btn-group flex-wrap" role="group" aria-label="Period">
            {PERIODS.map((option) => (
              <button
                key={option.value}
                type="button"
                className={`btn btn-sm ${period === option.value ? "btn-primary" : "btn-outline-dark"}`}
                aria-pressed={period === option.value}
                onClick={() => setPeriod(option.value)}
              >
                {option.label}
              </button>
            ))}
          </div>
          <select
            className="form-select form-select-sm spending-filters__account"
            aria-label="Account"
            value={accountId}
            onChange={(event) => setAccountId(event.target.value)}
          >
            <option value="">All accounts</option>
            {accounts.map(({ account, institution }) => (
              <option key={account.id} value={account.id}>
                {[institution, account.name, account.mask && `••${account.mask}`].filter(Boolean).join(" · ")}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div className="row g-4">
        <div className="col-lg-6">
          <CategoryChart filter={filter} selected={category} onSelect={setCategory} />
        </div>
        <div className="col-lg-6">
          <TopMerchantsChart filter={filter} category={category} onClearCategory={() => setCategory(null)} />
        </div>
        <div className="col-12">
          <MonthlyTrendChart filter={trendFilter} />
        </div>
      </div>
    </div>
  );
};

export default Spending;
