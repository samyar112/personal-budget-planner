const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, "") || "http://localhost:5211";

export type RegisterPayload = {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
};

export type RegisterResult = {
  id: string;
  email: string;
  name: string;
};

export type LoginPayload = {
  email: string;
  password: string;
};

export type LoginResult = {
  expiresAt: string;
  email: string;
  name: string;
};

/** Returned instead of cookies when a login code has been emailed. */
export type LoginChallenge = {
  challengeId: string;
  /** ISO time after which another code may be requested. */
  resendAvailableAt: string;
};

export type LoginOutcome =
  | { kind: "signedIn"; result: LoginResult }
  | { kind: "challenge"; challenge: LoginChallenge };

export type MeResult = {
  id: string;
  email: string;
  name: string;
  emailVerified: boolean;
};

export type ApiFieldErrors = Partial<
  Record<"firstName" | "lastName" | "email" | "password", string>
>;

export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors: ApiFieldErrors;
  /** Machine-readable reason from the API, e.g. "expired" for a stale verification link. */
  readonly code: string | null;

  constructor(
    message: string,
    status: number,
    fieldErrors: ApiFieldErrors = {},
    code: string | null = null,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.fieldErrors = fieldErrors;
    this.code = code;
  }
}

async function parseErrorBody(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return null;
  }
}

function throwApiError(response: Response, body: unknown): never {
  const messageFromBody =
    typeof body === "object" &&
    body !== null &&
    "message" in body &&
    typeof (body as { message: unknown }).message === "string"
      ? (body as { message: string }).message
      : null;

  if (response.status === 401) {
    throw new ApiError(messageFromBody ?? "Invalid email or password.", response.status);
  }

  if (response.status === 423) {
    throw new ApiError(
      messageFromBody ?? "Your account is temporarily locked. Please try again later.",
      response.status,
    );
  }

  if (response.status === 429) {
    throw new ApiError(
      messageFromBody ?? "Too many login attempts. Please try again later.",
      response.status,
    );
  }

  if (response.status === 409) {
    const message = messageFromBody ?? "An account with this email already exists.";
    throw new ApiError(message, response.status, { email: message });
  }

  if (response.status === 400 && typeof body === "object" && body !== null) {
    const errors = (body as { errors?: Record<string, string[]> }).errors;
    if (errors) {
      const fieldErrors: ApiFieldErrors = {};
      for (const [key, messages] of Object.entries(errors)) {
        const normalizedKey = key.charAt(0).toLowerCase() + key.slice(1);
        if (
          normalizedKey === "firstName" ||
          normalizedKey === "lastName" ||
          normalizedKey === "email" ||
          normalizedKey === "password"
        ) {
          fieldErrors[normalizedKey] = messages[0] ?? "Invalid value.";
        }
      }
      throw new ApiError("Please fix the highlighted fields.", response.status, fieldErrors);
    }
  }

  throw new ApiError("Something went wrong. Please try again.", response.status);
}

/** In-tab single-flight: concurrent 401s share one refresh call. */
let refreshInFlight: Promise<boolean> | null = null;

function shouldAttemptRefresh(path: string): boolean {
  return (
    !path.includes("/api/auth/login") &&
    !path.includes("/api/auth/register") &&
    !path.includes("/api/auth/refresh") &&
    !path.includes("/api/auth/logout")
  );
}

/** Mints a fresh access token so claims changed server-side (e.g. email verified) apply. */
export async function refreshSession(): Promise<boolean> {
  if (refreshInFlight) {
    return refreshInFlight;
  }

  refreshInFlight = (async () => {
    const response = await fetch(`${API_BASE_URL}/api/auth/refresh`, {
      method: "POST",
      credentials: "include",
    });
    return response.ok;
  })().finally(() => {
    refreshInFlight = null;
  });

  return refreshInFlight;
}

/**
 * Authenticated fetch with credentials and a single in-flight refresh retry on 401.
 */
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const url = path.startsWith("http") ? path : `${API_BASE_URL}${path}`;
  const response = await fetch(url, {
    ...init,
    credentials: "include",
  });

  if (response.status !== 401 || !shouldAttemptRefresh(path)) {
    return response;
  }

  const refreshed = await refreshSession();
  if (!refreshed) {
    return response;
  }

  return fetch(url, {
    ...init,
    credentials: "include",
  });
}

/** Registers a new email/password account. */
export async function registerUser(payload: RegisterPayload): Promise<RegisterResult> {
  const response = await fetch(`${API_BASE_URL}/api/auth/register`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });

  if (response.ok) {
    return (await response.json()) as RegisterResult;
  }

  throwApiError(response, await parseErrorBody(response));
}

/**
 * Logs in with email/password. A verified email gets a challenge (code emailed, no cookies yet);
 * otherwise the access JWT + opaque refresh are set as HttpOnly cookies right away.
 */
export async function loginUser(payload: LoginPayload): Promise<LoginOutcome> {
  const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });

  if (response.status === 202) {
    return { kind: "challenge", challenge: (await response.json()) as LoginChallenge };
  }

  if (response.ok) {
    return { kind: "signedIn", result: (await response.json()) as LoginResult };
  }

  throwApiError(response, await parseErrorBody(response));
}

/** Submits the emailed code. On success the HttpOnly cookies are set. */
export async function verifyLoginCode(challengeId: string, code: string): Promise<LoginResult> {
  const response = await fetch(`${API_BASE_URL}/api/auth/login/verify-code`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ challengeId, code }),
  });

  if (response.ok) {
    return (await response.json()) as LoginResult;
  }

  throwApiError(response, await parseErrorBody(response));
}

/** Emails a fresh code for the open challenge. */
export async function resendLoginCode(challengeId: string): Promise<LoginChallenge> {
  const response = await fetch(`${API_BASE_URL}/api/auth/login/resend-code`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ challengeId }),
  });

  if (response.ok) {
    return (await response.json()) as LoginChallenge;
  }

  throwApiError(response, await parseErrorBody(response));
}

/** Clears auth cookies and revokes the refresh token server-side. */
export async function logoutUser(): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/auth/logout`, {
    method: "POST",
    credentials: "include",
  });

  if (response.ok || response.status === 204) {
    return;
  }

  throwApiError(response, await parseErrorBody(response));
}

/** Fetches the current user; refreshes the session once on 401 when possible. */
export async function fetchCurrentUser(): Promise<MeResult> {
  const response = await apiFetch("/api/auth/me");

  if (response.ok) {
    return (await response.json()) as MeResult;
  }

  throwApiError(response, await parseErrorBody(response));
}

/** Consumes the emailed verification token. Rejects with code "expired" or "invalid". */
export async function verifyEmail(token: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/auth/verify-email`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ token }),
  });

  if (response.ok) {
    return;
  }

  const body = (await parseErrorBody(response)) as { message?: string; code?: string } | null;
  throw new ApiError(
    body?.message ?? "Something went wrong. Please try again.",
    response.status,
    {},
    body?.code ?? null,
  );
}

/** Asks for a fresh verification email. The API answers the same for every address. */
export async function resendVerification(email: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/auth/resend-verification`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ email }),
  });

  if (response.ok) {
    return;
  }

  if (response.status === 429) {
    throw new ApiError("Too many requests. Please wait a few minutes and try again.", 429);
  }

  throw new ApiError("Something went wrong. Please try again.", response.status);
}

/** Asks for a password-reset email. The API answers the same for every address. */
export async function requestPasswordReset(email: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/auth/forgot-password`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ email }),
  });

  if (response.ok) {
    return;
  }

  if (response.status === 429) {
    throw new ApiError("Too many requests. Please wait a few minutes and try again.", 429);
  }

  throw new ApiError("Something went wrong. Please try again.", response.status);
}

/** Sets a new password from the emailed token. Rejects with code "expired" or "invalid". */
export async function resetPassword(token: string, newPassword: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/auth/reset-password`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ token, newPassword }),
  });

  if (response.ok) {
    return;
  }

  const body = (await parseErrorBody(response)) as {
    message?: string;
    code?: string;
    errors?: Record<string, string[]>;
  } | null;
  const passwordError = body?.errors?.NewPassword?.[0] ?? body?.errors?.newPassword?.[0];
  throw new ApiError(
    passwordError ?? body?.message ?? "Something went wrong. Please try again.",
    response.status,
    {},
    body?.code ?? null,
  );
}
