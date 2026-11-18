import { ApiError, apiFetch } from "./auth";

export type LinkedAccount = { id: string; name: string };

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
