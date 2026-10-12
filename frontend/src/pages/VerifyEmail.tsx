import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ApiError, refreshSession, resendVerification, verifyEmail } from "../api/auth";
import AmbientBackground from "../components/AmbientBackground";
import BrandName from "../components/BrandName";

type Status =
  | { kind: "verifying" }
  | { kind: "verified" }
  | { kind: "failed"; message: string; expired: boolean };

/**
 * Landing page for the emailed link: /verify-email?token=...
 */
const VerifyEmail = () => {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [status, setStatus] = useState<Status>(
    token
      ? { kind: "verifying" }
      : { kind: "failed", message: "This verification link is missing its token.", expired: false },
  );
  const [email, setEmail] = useState("");
  const [resendMessage, setResendMessage] = useState("");
  const [resendError, setResendError] = useState("");
  const [resending, setResending] = useState(false);

  // Tokens are single-use, so StrictMode's double effect must not call the API twice.
  const attempted = useRef(false);

  useEffect(() => {
    if (!token || attempted.current) return;
    attempted.current = true;

    const run = async () => {
      try {
        await verifyEmail(token);
        // Already signed in on this browser? Pick up the verified claim now.
        await refreshSession().catch(() => false);
        setStatus({ kind: "verified" });
      } catch (err) {
        const apiError = err instanceof ApiError ? err : null;
        setStatus({
          kind: "failed",
          message: apiError?.message ?? "Something went wrong. Please try again.",
          expired: apiError?.code === "expired",
        });
      }
    };

    void run();
  }, [token]);

  const handleResend = async (event: FormEvent) => {
    event.preventDefault();
    if (!email.trim()) {
      setResendError("Enter the email you signed up with.");
      return;
    }

    setResending(true);
    setResendError("");
    setResendMessage("");
    try {
      await resendVerification(email.trim());
      setResendMessage("If that account needs verification, a new link is on its way.");
    } catch (err) {
      setResendError(err instanceof ApiError ? err.message : "Something went wrong. Please try again.");
    } finally {
      setResending(false);
    }
  };

  return (
    <div className="home-page">
      <AmbientBackground />
      <div className="container py-5" style={{ maxWidth: 520 }}>
        <div className="glass-panel p-4 text-center">
          <div className="mb-3">
            <BrandName logoSize="sm" />
          </div>

          {status.kind === "verifying" && (
            <p className="text-body-brand mb-0" role="status">
              Verifying your email…
            </p>
          )}

          {status.kind === "verified" && (
            <>
              <h1 className="h4 text-headline">Email verified</h1>
              <p className="text-body-brand">Your account is ready.</p>
              <Link className="btn btn-primary text-nav" to="/">
                Continue to log in
              </Link>
            </>
          )}

          {status.kind === "failed" && (
            <>
              <h1 className="h4 text-headline">
                {status.expired ? "Link expired" : "Link not valid"}
              </h1>
              <div className="alert alert-danger py-2" role="alert">
                {status.message}
              </div>

              <form onSubmit={handleResend} className="text-start" noValidate>
                <label className="form-label" htmlFor="resend-email">
                  Email
                </label>
                <input
                  id="resend-email"
                  type="email"
                  className="form-control mb-3"
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  autoComplete="email"
                />
                {resendError && (
                  <div className="alert alert-danger py-2" role="alert">
                    {resendError}
                  </div>
                )}
                {resendMessage && (
                  <div className="alert alert-success py-2" role="status">
                    {resendMessage}
                  </div>
                )}
                <button type="submit" className="btn btn-primary text-nav w-100" disabled={resending}>
                  {resending ? "Sending…" : "Send a new link"}
                </button>
              </form>
            </>
          )}
        </div>
      </div>
    </div>
  );
};

export default VerifyEmail;
