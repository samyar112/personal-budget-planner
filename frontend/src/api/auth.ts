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

export type ApiFieldErrors = Partial<
  Record<"firstName" | "lastName" | "email" | "password", string>
>;

export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors: ApiFieldErrors;

  constructor(message: string, status: number, fieldErrors: ApiFieldErrors = {}) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

/** Registers a new email/password account. */
export async function registerUser(payload: RegisterPayload): Promise<RegisterResult> {
  const response = await fetch(`${API_BASE_URL}/api/auth/register`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });

  if (response.ok) {
    return (await response.json()) as RegisterResult;
  }

  let body: unknown = null;
  try {
    body = await response.json();
  } catch {
    // Ignore empty/non-JSON error bodies.
  }

  if (response.status === 409) {
    const message =
      typeof body === "object" &&
      body !== null &&
      "message" in body &&
      typeof (body as { message: unknown }).message === "string"
        ? (body as { message: string }).message
        : "An account with this email already exists.";
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
