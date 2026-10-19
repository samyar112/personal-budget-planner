import { ApiError, apiFetch } from "./auth";

export type AdminUser = {
  id: string;
  email: string;
  name: string;
  role: "User" | "Admin";
  emailVerified: boolean;
  isActive: boolean;
  isLocked: boolean;
  /** ISO time the account was created. */
  createdAt: string;
};

export type AdminUserList = {
  items: AdminUser[];
  page: number;
  pageSize: number;
  total: number;
};

async function messageFrom(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { message?: unknown };
    return typeof body.message === "string" ? body.message : fallback;
  } catch {
    return fallback;
  }
}

/** One page of accounts. `verified: false` is the unverified-email list. */
export async function fetchAdminUsers(options: {
  verified?: boolean;
  page: number;
  pageSize?: number;
}): Promise<AdminUserList> {
  const params = new URLSearchParams({
    page: String(options.page),
    pageSize: String(options.pageSize ?? 50),
  });
  if (options.verified !== undefined) {
    params.set("verified", String(options.verified));
  }

  const response = await apiFetch(`/api/admin/users?${params}`);
  if (response.ok) {
    return (await response.json()) as AdminUserList;
  }

  throw new ApiError(
    await messageFrom(response, "Could not load users. Please try again."),
    response.status,
  );
}

/** Deactivating also ends the user's sessions. Admin accounts are refused with a 409. */
export async function setUserActive(id: string, active: boolean): Promise<void> {
  const response = await apiFetch(
    `/api/admin/users/${id}/${active ? "activate" : "deactivate"}`,
    { method: "POST" },
  );
  if (response.ok) {
    return;
  }

  throw new ApiError(
    await messageFrom(response, "Could not update the account. Please try again."),
    response.status,
  );
}
