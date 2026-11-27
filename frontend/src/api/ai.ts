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

/**
 * One roadmap step, computed by the rules and never written by the AI. `per` is "month" for a monthly amount or
 * "total" for a balance to reach; `by` is the estimated month (yyyy-MM-dd, first of the month).
 */
export type RoadmapStep = { label: string; amount: number; per: "month" | "total"; by: string | null };

/** One tip. `id` is the rule that chose it; `source` says whether this tip's wording is the AI's or ours. */
export type AiTip = { id: string; title: string; text: string; source: "ai" | "template"; steps: RoadmapStep[] };

/** "ai" when every tip is the AI's checked wording, "template" when none is, "mixed" otherwise. */
export type AiTipsResponse = {
  source: "ai" | "template" | "mixed";
  model: string | null;
  tips: AiTip[];
  dataGaps: string[];
  createdAt: string;
};

/** Sends the previewed payload for tips. Calling this is the user's consent to send it. */
export async function generateTips(): Promise<AiTipsResponse> {
  const response = await apiFetch("/api/ai/tips", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ consent: true }),
  });
  if (response.ok) {
    return (await response.json()) as AiTipsResponse;
  }

  throw new ApiError(
    response.status === 422
      ? "The privacy check blocked this request, so nothing was sent."
      : response.status === 429
        ? "You have generated tips the maximum number of times today. Try again tomorrow."
        : response.status === 403
          ? "Verify your email to generate tips."
          : response.status === 402
            ? "Your free trial has ended. Upgrade to keep generating AI tips."
            : "Could not generate tips. Try again.",
    response.status,
  );
}
