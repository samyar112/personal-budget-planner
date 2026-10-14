import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { ApiError, requestPasswordReset } from "../api/auth";
import AmbientBackground from "../components/AmbientBackground";
import BrandName from "../components/BrandName";

/**
 * Request step of the reset flow: /forgot-password. The API answers the same for every address,
 * so this page never says whether an account exists.
 */
const ForgotPassword = () => {
  const [email, setEmail] = useState("");
  const [error, setError] = useState("");
  const [sent, setSent] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (!email.trim()) {
      setError("Enter the email you signed up with.");
      return;
    }

    setSubmitting(true);
    setError("");
    try {
      await requestPasswordReset(email.trim());
      setSent(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong. Please try again.");
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
          <h1 className="h4 text-headline text-center">Forgot your password?</h1>

          {sent ? (
            <>
              <div className="alert alert-success py-2" role="status">
                If an account exists for that address, a reset email is on its way. The link
                expires in 15 minutes.
              </div>
              <div className="text-center">
                <Link className="btn btn-primary text-nav" to="/">
                  Back to log in
                </Link>
              </div>
            </>
          ) : (
            <form onSubmit={handleSubmit} noValidate>
              <p className="text-body-brand">
                Enter your email and we will send you a link to choose a new password.
              </p>
              <label className="form-label" htmlFor="forgot-email">
                Email
              </label>
              <input
                id="forgot-email"
                type="email"
                className="form-control mb-3"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                autoComplete="email"
              />
              {error && (
                <div className="alert alert-danger py-2" role="alert">
                  {error}
                </div>
              )}
              <button type="submit" className="btn btn-primary text-nav w-100" disabled={submitting}>
                {submitting ? "Sending…" : "Send reset link"}
              </button>
              <div className="text-center mt-3">
                <Link to="/" className="small text-primary text-decoration-none">
                  Back to log in
                </Link>
              </div>
            </form>
          )}
        </div>
      </div>
    </div>
  );
};

export default ForgotPassword;
