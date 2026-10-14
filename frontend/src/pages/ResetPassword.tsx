import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ApiError, resetPassword } from "../api/auth";
import AmbientBackground from "../components/AmbientBackground";
import BrandName from "../components/BrandName";

/** Mirrors the server rule (AuthValidation.IsPasswordValid) and the signup checklist. */
const isStrongPassword = (password: string) =>
  password.length >= 8 &&
  /[A-Z]/.test(password) &&
  /[a-z]/.test(password) &&
  /\d/.test(password) &&
  /[^A-Za-z0-9]/.test(password);

const RULES_HINT =
  "At least 8 characters with an uppercase letter, a lowercase letter, a number and a special character.";

/**
 * Landing page for the emailed link: /reset-password?token=...
 */
const ResetPassword = () => {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [linkProblem, setLinkProblem] = useState(
    token ? "" : "This reset link is missing its token.",
  );
  const [done, setDone] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (!isStrongPassword(password)) {
      setError(RULES_HINT);
      return;
    }
    if (password !== confirm) {
      setError("Passwords do not match.");
      return;
    }

    setSubmitting(true);
    setError("");
    try {
      await resetPassword(token, password);
      setDone(true);
    } catch (err) {
      const apiError = err instanceof ApiError ? err : null;
      // A bad or used link cannot be fixed by retyping the password.
      if (apiError?.code === "expired" || apiError?.code === "invalid") {
        setLinkProblem(apiError.message);
      } else {
        setError(apiError?.message ?? "Something went wrong. Please try again.");
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="home-page">
      <AmbientBackground />
      <div className="container py-5" style={{ maxWidth: 520 }}>
        <div className="glass-panel p-4">
          <div className="mb-3 text-center">
            <BrandName logoSize="sm" />
          </div>

          {done && (
            <div className="text-center">
              <h1 className="h4 text-headline">Password updated</h1>
              <p className="text-body-brand">
                You have been signed out everywhere. Log in with your new password.
              </p>
              <Link className="btn btn-primary text-nav" to="/">
                Continue to log in
              </Link>
            </div>
          )}

          {!done && linkProblem && (
            <div className="text-center">
              <h1 className="h4 text-headline">Link not valid</h1>
              <div className="alert alert-danger py-2" role="alert">
                {linkProblem}
              </div>
              <Link className="btn btn-primary text-nav" to="/forgot-password">
                Request a new link
              </Link>
            </div>
          )}

          {!done && !linkProblem && (
            <form onSubmit={handleSubmit} noValidate>
              <h1 className="h4 text-headline text-center">Choose a new password</h1>
              <label className="form-label" htmlFor="new-password">
                New password
              </label>
              <div className="password-field mb-1">
                <input
                  id="new-password"
                  type={showPassword ? "text" : "password"}
                  className="form-control"
                  autoComplete="new-password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                />
                <button
                  type="button"
                  className="password-field__toggle"
                  onClick={() => setShowPassword((visible) => !visible)}
                  aria-label={showPassword ? "Hide password" : "Show password"}
                >
                  {showPassword ? "Hide" : "Show"}
                </button>
              </div>
              <p className="small text-body-brand">{RULES_HINT}</p>

              <label className="form-label" htmlFor="confirm-password">
                Confirm new password
              </label>
              <input
                id="confirm-password"
                type={showPassword ? "text" : "password"}
                className="form-control mb-3"
                autoComplete="new-password"
                value={confirm}
                onChange={(event) => setConfirm(event.target.value)}
              />

              {error && (
                <div className="alert alert-danger py-2" role="alert">
                  {error}
                </div>
              )}
              <button type="submit" className="btn btn-primary text-nav w-100" disabled={submitting}>
                {submitting ? "Saving…" : "Reset password"}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
};

export default ResetPassword;
