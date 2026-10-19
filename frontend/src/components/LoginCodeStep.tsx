import { useEffect, useState, type FormEvent } from "react";
import {
  ApiError,
  resendLoginCode,
  verifyLoginCode,
  type LoginChallenge,
  type LoginResult,
} from "../api/auth";

type LoginCodeStepProps = {
  email: string;
  challenge: LoginChallenge;
  onVerified: (result: LoginResult) => void;
  /** Back to the password form, e.g. when the challenge is spent or expired. */
  onCancel: () => void;
};

/** Longest cooldown the UI will show, so a skewed device clock cannot strand the button. */
const MAX_COOLDOWN_MS = 60_000;

function cooldownLeft(resendAvailableAt: string): number {
  return Math.min(Math.max(Date.parse(resendAvailableAt) - Date.now(), 0), MAX_COOLDOWN_MS);
}

/** Second step of login: enter the 6-digit code from the email, or ask for a new one. */
const LoginCodeStep = ({ email, challenge, onVerified, onCancel }: LoginCodeStepProps) => {
  const [code, setCode] = useState("");
  const [error, setError] = useState("");
  const [info, setInfo] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [resending, setResending] = useState(false);
  const [resendAvailableAt, setResendAvailableAt] = useState(challenge.resendAvailableAt);
  const [cooldownMs, setCooldownMs] = useState(() => cooldownLeft(challenge.resendAvailableAt));

  useEffect(() => {
    const timer = window.setInterval(() => {
      const left = cooldownLeft(resendAvailableAt);
      setCooldownMs(left);
      if (left === 0) window.clearInterval(timer);
    }, 500);
    return () => window.clearInterval(timer);
  }, [resendAvailableAt]);

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (submitting) return;
    if (!/^\d{6}$/.test(code)) {
      setError("Enter the 6-digit code from your email.");
      return;
    }

    setSubmitting(true);
    setError("");
    setInfo("");
    try {
      onVerified(await verifyLoginCode(challenge.challengeId, code));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong. Please try again.");
      setCode("");
    } finally {
      setSubmitting(false);
    }
  };

  const handleResend = async () => {
    if (resending || cooldownMs > 0) return;
    setResending(true);
    setError("");
    setInfo("");
    try {
      const next = await resendLoginCode(challenge.challengeId);
      setResendAvailableAt(next.resendAvailableAt);
      setCooldownMs(cooldownLeft(next.resendAvailableAt));
      setInfo("We sent a new code.");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong. Please try again.");
    } finally {
      setResending(false);
    }
  };

  const seconds = Math.ceil(cooldownMs / 1000);

  return (
    <form onSubmit={handleSubmit} noValidate>
      <p className="text-body-brand">
        We emailed a 6-digit code to <strong>{email}</strong>. It expires in 10 minutes.
      </p>

      <label className="form-label" htmlFor="login-code">
        Login code
      </label>
      <input
        id="login-code"
        name="code"
        type="text"
        inputMode="numeric"
        pattern="[0-9]*"
        maxLength={6}
        autoComplete="one-time-code"
        autoFocus
        className="form-control mb-3"
        value={code}
        onChange={(event) => setCode(event.target.value.replace(/\D/g, ""))}
      />

      {error && (
        <div className="alert alert-danger py-2" role="alert">
          {error}
        </div>
      )}
      {info && (
        <div className="alert alert-success py-2" role="status">
          {info}
        </div>
      )}

      <button type="submit" className="btn btn-primary w-100 text-nav" disabled={submitting}>
        {submitting ? "Checking..." : "Verify and log in"}
      </button>

      <div className="d-flex justify-content-between mt-3">
        <button
          type="button"
          className="btn btn-link p-0 text-decoration-none"
          onClick={handleResend}
          disabled={resending || cooldownMs > 0}
        >
          {cooldownMs > 0 ? `Resend code in ${seconds}s` : "Resend code"}
        </button>
        <button
          type="button"
          className="btn btn-link p-0 text-decoration-none"
          onClick={onCancel}
        >
          Back to log in
        </button>
      </div>
    </form>
  );
};

export default LoginCodeStep;
