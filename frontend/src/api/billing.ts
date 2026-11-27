import { ApiError, apiFetch } from "./auth";

async function redirectUrl(path: string, failure: string): Promise<string> {
  const response = await apiFetch(path, { method: "POST" });
  if (response.ok) {
    return ((await response.json()) as { url: string }).url;
  }

  throw new ApiError(
    response.status === 409
      ? "You are already subscribed."
      : response.status === 503
        ? "Payments are not set up yet."
        : failure,
    response.status,
  );
}

/** A Stripe-hosted Checkout page for the $2.99/month plan. Card details never touch VaultBudget. */
export function startCheckout(): Promise<string> {
  return redirectUrl("/api/billing/checkout", "Could not start checkout. Try again.");
}

/** Stripe's billing portal, to cancel or change the card. */
export function openBillingPortal(): Promise<string> {
  return redirectUrl("/api/billing/portal", "Could not open billing. Try again.");
}
