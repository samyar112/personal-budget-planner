import { ApiError, apiFetch } from "./auth";

export type LinkedAccount = {
  id: string;
  name: string;
  /** Last 4 digits, for display only. */
  mask: string | null;
  /** Plaid account type: depository, credit, loan, investment or other. */
  type: string | null;
};

export type PlaidItem = {
  id: string;
  institution: string | null;
  /** ISO timestamp, or null until the first sync commits. */
  lastSyncedAt: string | null;
  accounts: LinkedAccount[];
};

export type PlaidSyncResult = { added: number; modified: number; removed: number; lastSyncedAt: string };

/** The caller's linked banks and their accounts. */
export async function fetchPlaidItems(): Promise<PlaidItem[]> {
  const response = await apiFetch("/api/plaid/items");
  if (response.ok) {
    return (await response.json()) as PlaidItem[];
  }

  throw new ApiError(
    response.status === 403
      ? "Verify your email to see your connected banks."
      : "Could not load your connected banks. Try again.",
    response.status,
  );
}

/** Pulls new, changed and removed transactions for one linked bank into the ledger. */
export async function syncPlaidItem(id: string): Promise<PlaidSyncResult> {
  const response = await apiFetch(`/api/plaid/items/${encodeURIComponent(id)}/sync`, { method: "POST" });
  if (response.ok) {
    return (await response.json()) as PlaidSyncResult;
  }

  throw new ApiError(
    response.status === 409
      ? "A sync for this bank is already running."
      : "Could not sync this bank. Try again.",
    response.status,
  );
}

export type PlaidExchangeResult = {
  institution: string | null;
  accounts: LinkedAccount[];
};

/** A short-lived token that only opens Plaid Link for the signed-in user. */
export async function createLinkToken(): Promise<string> {
  const response = await apiFetch("/api/plaid/link-token", { method: "POST" });
  if (response.ok) {
    const body = (await response.json()) as { linkToken: string };
    return body.linkToken;
  }

  throw new ApiError(
    response.status === 403
      ? "Verify your email before connecting a bank."
      : "Could not start the bank connection. Try again.",
    response.status,
  );
}

/**
 * Hands the one-time public token from Plaid Link to the API. The API keeps the resulting access
 * token; the browser only ever gets back account names.
 */
export async function exchangePublicToken(publicToken: string): Promise<PlaidExchangeResult> {
  const response = await apiFetch("/api/plaid/exchange", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ publicToken }),
  });
  if (response.ok) {
    return (await response.json()) as PlaidExchangeResult;
  }

  throw new ApiError(
    response.status === 400
      ? "The bank link could not be completed. Try linking again."
      : "Could not save the bank connection. Try again.",
    response.status,
  );
}
