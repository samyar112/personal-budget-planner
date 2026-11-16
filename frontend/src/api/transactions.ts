import { ApiError, apiFetch } from "./auth";

export type Transaction = {
  id: string;
  accountId: string;
  source: "Plaid" | "PdfUpload" | "Seed";
  /** yyyy-MM-dd, no time zone. */
  date: string;
  description: string;
  /** Signed: negative is money out. */
  amount: number;
  category: string | null;
};

export type TransactionList = {
  items: Transaction[];
  page: number;
  pageSize: number;
  total: number;
};

const MAX_PAGE_SIZE = 100;

/** One page of the caller's ledger, newest first. Dates are inclusive yyyy-MM-dd. */
export async function fetchTransactions(options: {
  from?: string;
  to?: string;
  category?: string;
  page?: number;
  pageSize?: number;
}): Promise<TransactionList> {
  const params = new URLSearchParams({
    page: String(options.page ?? 1),
    pageSize: String(options.pageSize ?? 50),
  });
  if (options.from) params.set("from", options.from);
  if (options.to) params.set("to", options.to);
  if (options.category) params.set("category", options.category);

  const response = await apiFetch(`/api/transactions?${params}`);
  if (response.ok) {
    return (await response.json()) as TransactionList;
  }

  throw new ApiError(
    response.status === 403
      ? "Verify your email to see your transactions."
      : "Could not load transactions. Try again.",
    response.status,
  );
}

/** Every row in a date range, fetched page by page until the reported total is reached. */
export async function fetchAllTransactions(range: { from: string; to: string }): Promise<Transaction[]> {
  const rows: Transaction[] = [];
  for (let page = 1; ; page++) {
    const result = await fetchTransactions({ ...range, page, pageSize: MAX_PAGE_SIZE });
    rows.push(...result.items);
    if (result.items.length === 0 || rows.length >= result.total) {
      return rows;
    }
  }
}

export type LedgerSummary = { income: number; expenses: number; savings: number };

/** Income is money in, expenses is money out as a positive number, savings is what is left. */
export function summarize(rows: Transaction[]): LedgerSummary {
  let income = 0;
  let expenses = 0;
  for (const row of rows) {
    if (row.amount >= 0) income += row.amount;
    else expenses += -row.amount;
  }
  return { income, expenses, savings: income - expenses };
}

/** Local calendar date as yyyy-MM-dd. Not toISOString, which shifts to UTC. */
export function toDateString(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

export function formatMoney(amount: number): string {
  return money.format(amount);
}

/** yyyy-MM-dd to MM/dd/yyyy by splitting the string, so no time zone can move the day. */
export function formatDate(date: string): string {
  const [year, month, day] = date.split("-");
  return `${month}/${day}/${year}`;
}
