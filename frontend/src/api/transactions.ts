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

export type AccountSummary = { accountId: string; income: number; expenses: number; net: number };

/**
 * Income, expenses and net cash flow for a date range, per account and in total, computed by the server.
 * Internal transfers and credit card payments are left out, and the totals are the sum of the accounts.
 * Net cash flow is money left over, not money moved into savings. Refunds reduce expenses.
 */
export type TransactionSummary = { income: number; expenses: number; netCashFlow: number; accounts: AccountSummary[] };

/** The caller's summary for an inclusive yyyy-MM-dd range. */
export async function fetchSummary(range: { from: string; to: string }): Promise<TransactionSummary> {
  const response = await apiFetch(`/api/transactions/summary?${new URLSearchParams(range)}`);
  if (response.ok) {
    return (await response.json()) as TransactionSummary;
  }

  throw new ApiError(
    response.status === 403
      ? "Verify your email to see your transactions."
      : "Could not load your summary. Try again.",
    response.status,
  );
}

/** A date range plus an optional account, shared by every spending chart. Dates are inclusive yyyy-MM-dd. */
export type SpendingFilter = { from: string; to: string; accountId?: string };

export type CategorySpending = { category: string; amount: number };

/** Spending by category, largest first. `total` equals the summary's expenses for the same filter. */
export type SpendingByCategory = { total: number; categories: CategorySpending[] };

export type MonthlyCashFlow = { year: number; month: number; income: number; expenses: number; netCashFlow: number };

export type MerchantSpending = { merchant: string; amount: number; count: number };

async function getSpending<T>(path: string, params: Record<string, string | undefined>): Promise<T> {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) query.set(key, value);
  }

  const response = await apiFetch(`/api/transactions/spending/${path}?${query}`);
  if (response.ok) {
    return (await response.json()) as T;
  }

  throw new ApiError(
    response.status === 403
      ? "Verify your email to see your spending."
      : response.status === 404
        ? "That account is no longer linked."
        : "Could not load your spending. Try again.",
    response.status,
  );
}

export function fetchSpendingByCategory(filter: SpendingFilter): Promise<SpendingByCategory> {
  return getSpending("categories", filter);
}

/** Every month in the range, oldest first, including empty months. The range is capped at 24 months. */
export async function fetchMonthlyCashFlow(filter: SpendingFilter): Promise<MonthlyCashFlow[]> {
  return (await getSpending<{ months: MonthlyCashFlow[] }>("monthly", filter)).months;
}

/** The largest payees by spending, net of refunds. `category` narrows to one category code. */
export async function fetchTopMerchants(
  filter: SpendingFilter & { category?: string; limit?: number },
): Promise<MerchantSpending[]> {
  const { limit, ...rest } = filter;
  return (await getSpending<{ merchants: MerchantSpending[] }>("merchants", { ...rest, limit: limit?.toString() })).merchants;
}

const CATEGORY_LABELS: Record<string, string> = {
  INCOME: "Income",
  TRANSFER_IN: "Transfers in",
  TRANSFER_OUT: "Cash & payments to people",
  LOAN_PAYMENTS: "Loan payments",
  BANK_FEES: "Bank fees",
  ENTERTAINMENT: "Entertainment",
  FOOD_AND_DRINK: "Food & drink",
  GENERAL_MERCHANDISE: "Shopping",
  HOME_IMPROVEMENT: "Home improvement",
  MEDICAL: "Medical",
  PERSONAL_CARE: "Personal care",
  GENERAL_SERVICES: "Services",
  GOVERNMENT_AND_NON_PROFIT: "Government & charity",
  TRANSPORTATION: "Transportation",
  TRAVEL: "Travel",
  RENT_AND_UTILITIES: "Rent & utilities",
  OTHER: "Other",
};

/** A readable name for a ledger category code; unknown codes fall back to "Other". */
export function categoryLabel(code: string | null): string {
  return (code && CATEGORY_LABELS[code]) ?? "Other";
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
