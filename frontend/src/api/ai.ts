import { ApiError, apiFetch } from "./auth";

/** One computed figure. Null when there is not enough data yet. */
export type PayloadMetric = { id: string; value: number | null; unit: "USD" | "ratio" };

/** The only thing that may leave VaultBudget for an AI provider: numbers, dates and our own codes. */
export type AiPayload = {
  schemaVersion: string;
  asOf: string;
  coverage: { accountTypes: string[]; monthsComplete: number };
  metrics: PayloadMetric[];
  categories: { code: string; monthlyMean: number }[];
  recurring: { category: string; cadence: string; monthlyCost: number; isSubscription: boolean }[];
};

/** One of the user's real rows and the category bucket it is reduced to. Shown to the user only. */
export type PrivacyExample = {
  date: string;
  description: string;
  accountName: string;
  mask: string | null;
  amount: number;
  sentAs: string;
};

export type PrivacyCheck = { id: string; label: string; hits: number; passed: boolean };

export type PayloadPrivacyReport = {
  passed: boolean;
  examples: PrivacyExample[];
  withheld: { field: string; count: number }[];
  checks: PrivacyCheck[];
};

/** `payloadJson` is the exact serialised payload from the server, not a re-serialisation. */
export type AiPayloadPreview = { payload: AiPayload; payloadJson: string; report: PayloadPrivacyReport };

/** What the AI would receive, and what stayed behind. Nothing is sent to any AI provider by this call. */
export async function fetchPayloadPreview(): Promise<AiPayloadPreview> {
  const response = await apiFetch("/api/ai/payload-preview");
  if (response.ok) {
    return (await response.json()) as AiPayloadPreview;
  }

  throw new ApiError(
    response.status === 403
      ? "Verify your email to see your privacy preview."
      : "Could not load the privacy preview. Try again.",
    response.status,
  );
}
