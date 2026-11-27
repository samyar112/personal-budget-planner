import { ApiError, apiFetch } from "./auth";
import type { Transaction } from "./transactions";

/** A statement account. Its rows stay out of every total until merged. `mask` is the last four digits only. */
export type StatementAccount = {
  id: string;
  name: string;
  mask: string | null;
  transactionCount: number;
  /** yyyy-MM-dd, or null when the account has no rows. */
  firstDate: string | null;
  lastDate: string | null;
};

export type StatementDetail = {
  account: StatementAccount;
  income: number;
  expenses: number;
  net: number;
  transactions: Transaction[];
};

export type StatementUploadResult = { accountId: string; imported: number; skipped: number };

/** The server's own message for a rejected file, from the ValidationProblemDetails body. */
async function rejection(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { errors?: Record<string, string[]> };
    return body.errors?.file?.[0] ?? null;
  } catch {
    return null;
  }
}

/** Uploads one PDF statement. The file is parsed on the server and never stored. */
export async function uploadStatement(file: File): Promise<StatementUploadResult> {
  const body = new FormData();
  body.append("file", file);
  const response = await apiFetch("/api/statements", { method: "POST", body });
  if (response.ok) {
    return (await response.json()) as StatementUploadResult;
  }

  if (response.status === 400) {
    throw new ApiError((await rejection(response)) ?? "That file could not be read as a statement.", 400);
  }

  throw new ApiError(
    response.status === 413 ? "That file is too large." : "Could not upload the statement. Try again.",
    response.status,
  );
}

export async function fetchStatementAccounts(): Promise<StatementAccount[]> {
  const response = await apiFetch("/api/statements");
  if (response.ok) {
    return (await response.json()) as StatementAccount[];
  }

  throw new ApiError("Could not load your statements. Try again.", response.status);
}

export async function fetchStatement(id: string): Promise<StatementDetail> {
  const response = await apiFetch(`/api/statements/${encodeURIComponent(id)}`);
  if (response.ok) {
    return (await response.json()) as StatementDetail;
  }

  throw new ApiError("Could not load this statement. Try again.", response.status);
}
