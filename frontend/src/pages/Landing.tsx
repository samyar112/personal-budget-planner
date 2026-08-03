import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import BrandName, {
  AUTH_GOOGLE_STUB_KEY,
  AUTH_USER_STORAGE_KEY,
  BRAND_NAME,
} from "../components/BrandName";
import AmbientBackground from "../components/AmbientBackground";
import GoogleSignInButton from "../components/GoogleSignInButton";
import { ApiError, loginUser, registerUser } from "../api/auth";
import "./Landing.css";

type FormState = {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
};

/** Returns which password policy rules the current value satisfies (for the live checklist). */
const getPasswordChecks = (password: string) => ({
  minLength: password.length >= 8,
  hasUpper: /[A-Z]/.test(password),
  hasLower: /[a-z]/.test(password),
  hasNumber: /\d/.test(password),
  hasSpecial: /[^A-Za-z0-9]/.test(password),
});

/** Letters (incl. accents); spaces, hyphens, and apostrophes allowed between name parts. */
const NAME_PATTERN = /^[\p{L}]+(?:[ '\-][\p{L}]+)*$/u;

/** Validates a name field and returns an inline error message, or "" if valid. */
const getNameError = (value: string, label: string) => {
  const trimmed = value.trim();
  if (!trimmed) return `${label} is required.`;
  if (trimmed.length > 50) return `${label} must be 50 characters or less.`;
  if (!NAME_PATTERN.test(trimmed)) {
    return `Enter a valid ${label.toLowerCase()}.`;
  }
  return "";
};

const emptyForm: FormState = {
  firstName: "",
  lastName: "",
  email: "",
  password: "",
};

type FieldErrors = {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
};

const emptyFieldErrors: FieldErrors = {
  firstName: "",
  lastName: "",
  email: "",
  password: "",
};

/** Static marketing feature cards shown below the hero. */
const features = [
  {
    icon: "🔒",
    title: "Private by Design",
    desc: "All personal data is minimized before AI analysis. We prioritize privacy and security.",
  },
  {
    icon: "🤖",
    title: "AI-Powered Insights",
    desc: "Get personalized budget recommendations based on real spending patterns.",
  },
  {
    icon: "📊",
    title: "Spending Breakdown",
    desc: "Visualize where your money goes with clear charts and insights.",
  },
  {
    icon: "💳",
    title: "Multi-Account Tracking",
    desc: "Connect multiple cards and accounts in one dashboard.",
  },
  {
    icon: "🔔",
    title: "Bill Reminders",
    desc: "Get alerts before subscriptions and bills are due.",
  },
  {
    icon: "📈",
    title: "Financial Score",
    desc: "A 0–100 score based on savings, spending, and debt habits.",
  },
];

/**
 * Public landing page: marketing hero + auth card (login / sign up).
 * Sign up → POST /api/auth/register; login → POST /api/auth/login (HttpOnly JWT cookie).
 */
const Landing = () => {
  const navigate = useNavigate();

  const [isLogin, setIsLogin] = useState(true);
  const [loading, setLoading] = useState(false);
  const [googleLoading, setGoogleLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string>("");
  const [successMessage, setSuccessMessage] = useState<string>("");
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>(emptyFieldErrors);
  const isAuthBusy = loading || googleLoading;

  const [form, setForm] = useState<FormState>(emptyForm);

  const passwordChecks = getPasswordChecks(form.password);
  const isPasswordValid = Object.values(passwordChecks).every(Boolean);

  /** Clears all per-field validation messages. */
  const clearFieldErrors = () => setFieldErrors(emptyFieldErrors);

  /** Switches Login ↔ Sign Up and resets form, password visibility, and errors. */
  const switchAuthMode = (nextIsLogin: boolean, options?: { preserveSuccess?: boolean }) => {
    setIsLogin(nextIsLogin);
    setForm(emptyForm);
    setShowPassword(false);
    setError("");
    if (!options?.preserveSuccess) {
      setSuccessMessage("");
    }
    clearFieldErrors();
  };

  /** Updates a form field and clears that field's error as the user types. */
  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value } = e.target;
    setForm((prev) => ({
      ...prev,
      [name]: value,
    }));

    if (name in fieldErrors && fieldErrors[name as keyof FieldErrors]) {
      setFieldErrors((prev) => ({ ...prev, [name]: "" }));
    }
  };

  /**
   * Runs client-side auth validation.
   * Puts errors under each field; returns true only when the form can submit.
   */
  const validate = () => {
    setError("");
    const nextErrors: FieldErrors = { ...emptyFieldErrors };
    let isValid = true;

    if (!isLogin) {
      const firstNameError = getNameError(form.firstName, "First name");
      const lastNameError = getNameError(form.lastName, "Last name");

      if (firstNameError) {
        nextErrors.firstName = firstNameError;
        isValid = false;
      }
      if (lastNameError) {
        nextErrors.lastName = lastNameError;
        isValid = false;
      }
    }

    if (!form.email.trim()) {
      nextErrors.email = "Email is required.";
      isValid = false;
    } else if (!/\S+@\S+\.\S+/.test(form.email)) {
      nextErrors.email = "Please enter a valid email address.";
      isValid = false;
    }

    if (!form.password) {
      nextErrors.password = "Password is required.";
      isValid = false;
    } else if (!isLogin && !isPasswordValid) {
      nextErrors.password = "Password does not meet all requirements.";
      isValid = false;
    }

    setFieldErrors(nextErrors);
    return isValid;
  };

  /** Caches display profile and navigates to the dashboard. JWT lives in HttpOnly cookie. */
  const completeSignIn = (
    user: { name: string; email: string; picture?: string; provider: "email" | "google" },
  ) => {
    localStorage.setItem(AUTH_USER_STORAGE_KEY, JSON.stringify(user));

    if (user.provider === "google") {
      localStorage.setItem(AUTH_GOOGLE_STUB_KEY, "true");
    } else {
      localStorage.removeItem(AUTH_GOOGLE_STUB_KEY);
    }

    navigate("/home");
  };

  /** Handles a successful Google profile fetch from the Google sign-in button. */
  const handleGoogleSignIn = (user: { name: string; email: string; picture?: string }) => {
    setError("");
    setSuccessMessage("");
    clearFieldErrors();
    completeSignIn({ ...user, provider: "google" });
  };

  /**
   * Submits login/sign-up after validation.
   * Sign up → register API; login → login API (sets HttpOnly cookie).
   */
  const handleSubmit: React.FormEventHandler<HTMLFormElement> = async (e) => {
    e.preventDefault();
    setSuccessMessage("");

    if (!validate()) return;

    setLoading(true);
    setError("");

    try {
      if (!isLogin) {
        await registerUser({
          firstName: form.firstName.trim(),
          lastName: form.lastName.trim(),
          email: form.email.trim(),
          password: form.password,
        });

        switchAuthMode(true, { preserveSuccess: true });
        setSuccessMessage("Account created. Please log in.");
        return;
      }

      const result = await loginUser({
        email: form.email.trim(),
        password: form.password,
      });

      completeSignIn({
        name: result.name,
        email: result.email,
        provider: "email",
      });
    } catch (err) {
      if (err instanceof ApiError) {
        const hasFieldErrors = Object.keys(err.fieldErrors).length > 0;
        if (hasFieldErrors) {
          // Show under the field only — do not also show the form banner.
          setFieldErrors((prev) => ({
            ...prev,
            ...err.fieldErrors,
          }));
        } else {
          setError(err.message);
        }
      } else {
        setError("Something went wrong. Please try again.");
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="landing-page">
      <AmbientBackground />

      <nav className="navbar navbar-expand-lg glass-panel landing-nav">
        <div className="container-fluid px-4 px-lg-5">
          <Link className="navbar-brand text-nav" to="/">
            <BrandName logoSize="sm" />
          </Link>

          <button
            className="navbar-toggler"
            type="button"
            data-bs-toggle="collapse"
            data-bs-target="#landingNav"
            aria-controls="landingNav"
            aria-expanded="false"
            aria-label="Toggle navigation"
          >
            <span className="navbar-toggler-icon" />
          </button>

          <div className="collapse navbar-collapse" id="landingNav">
            <ul className="navbar-nav ms-auto gap-2 gap-lg-3">
              <li className="nav-item">
                <Link className="nav-link text-nav" to="/home">
                  Dashboard
                </Link>
              </li>
              <li className="nav-item">
                <a className="nav-link text-nav" href="#features">
                  Features
                </a>
              </li>
              <li className="nav-item">
                <a className="nav-link text-nav" href="#pricing">
                  Pricing
                </a>
              </li>
              <li className="nav-item">
                <a className="nav-link text-nav" href="#about">
                  About
                </a>
              </li>
            </ul>
          </div>
        </div>
      </nav>

      <section className="landing-hero">
        <div className="container">
          <div className="row align-items-center g-5 py-5 py-lg-0">
            <div className="col-lg-6">
              <span className="badge badge-eyebrow mb-3">AI-Powered Budgeting</span>

              <h1 className="text-headline mb-3">
                Your money, <br />
                <span className="text-primary">finally clear.</span>
              </h1>

              <p className="text-body-brand text-body-secondary mb-4 landing-subhead">
                {BRAND_NAME} helps you understand spending, reduce waste,
                and build better financial habits—securely and privately.
              </p>

              <div className="landing-stats d-flex align-items-center gap-4">
                <div className="landing-stat">
                  <span className="landing-stat__num">10k+</span>
                  <span className="landing-stat__label text-body-secondary">Users</span>
                </div>
                <div className="landing-stat__divider" />
                <div className="landing-stat">
                  <span className="landing-stat__num">$2.4M</span>
                  <span className="landing-stat__label text-body-secondary">Saved</span>
                </div>
                <div className="landing-stat__divider" />
                <div className="landing-stat">
                  <span className="landing-stat__num">98%</span>
                  <span className="landing-stat__label text-body-secondary">Privacy-first</span>
                </div>
              </div>
            </div>

            <div className="col-lg-6">
              <div className="auth-card glass-card glass-card--strong">
                <div className="text-center mb-4">
                  <BrandName layout="stacked" logoSize="md" showLogo />
                </div>

                <ul className="nav nav-pills nav-fill auth-pills mb-4" role="tablist">
                  <li className="nav-item" role="presentation">
                    <button
                      type="button"
                      className={`nav-link w-100 ${isLogin ? "active" : ""}`}
                      role="tab"
                      aria-selected={isLogin}
                      onClick={() => switchAuthMode(true)}
                    >
                      Login
                    </button>
                  </li>
                  <li className="nav-item" role="presentation">
                    <button
                      type="button"
                      className={`nav-link w-100 ${!isLogin ? "active" : ""}`}
                      role="tab"
                      aria-selected={!isLogin}
                      onClick={() => switchAuthMode(false)}
                    >
                      Sign Up
                    </button>
                  </li>
                </ul>

                <form onSubmit={handleSubmit} noValidate>
                  {!isLogin && (
                    <div className="row g-2 mb-3">
                      <div className="col-6">
                        <label htmlFor="firstName" className="form-label">
                          First Name
                        </label>
                        <input
                          id="firstName"
                          name="firstName"
                          type="text"
                          className={`form-control${fieldErrors.firstName ? " is-invalid" : ""}`}
                          placeholder="Jane"
                          autoComplete="given-name"
                          maxLength={50}
                          aria-invalid={Boolean(fieldErrors.firstName)}
                          value={form.firstName}
                          onChange={handleChange}
                        />
                        {fieldErrors.firstName && (
                          <div className="field-error">{fieldErrors.firstName}</div>
                        )}
                      </div>
                      <div className="col-6">
                        <label htmlFor="lastName" className="form-label">
                          Last Name
                        </label>
                        <input
                          id="lastName"
                          name="lastName"
                          type="text"
                          className={`form-control${fieldErrors.lastName ? " is-invalid" : ""}`}
                          placeholder="Smith"
                          autoComplete="family-name"
                          maxLength={50}
                          aria-invalid={Boolean(fieldErrors.lastName)}
                          value={form.lastName}
                          onChange={handleChange}
                        />
                        {fieldErrors.lastName && (
                          <div className="field-error">{fieldErrors.lastName}</div>
                        )}
                      </div>
                    </div>
                  )}

                  <div className="mb-3">
                    <label htmlFor="email" className="form-label">
                      Email
                    </label>
                    <input
                      id="email"
                      name="email"
                      type="email"
                      className={`form-control${fieldErrors.email ? " is-invalid" : ""}`}
                      placeholder="you@example.com"
                      autoComplete="email"
                      aria-invalid={Boolean(fieldErrors.email)}
                      aria-describedby={fieldErrors.email ? "email-error" : undefined}
                      value={form.email}
                      onChange={handleChange}
                    />
                    {fieldErrors.email && (
                      <div id="email-error" className="field-error">
                        {fieldErrors.email}
                      </div>
                    )}
                  </div>

                  <div className="mb-3">
                    <label htmlFor="password" className="form-label">
                      Password
                    </label>
                    <div className="password-field">
                      <input
                        id="password"
                        name="password"
                        type={showPassword ? "text" : "password"}
                        className={`form-control${fieldErrors.password ? " is-invalid" : ""}`}
                        placeholder="••••••••"
                        autoComplete={isLogin ? "current-password" : "new-password"}
                        aria-invalid={Boolean(fieldErrors.password)}
                        value={form.password}
                        onChange={handleChange}
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

                    {fieldErrors.password && (
                      <div className="field-error">{fieldErrors.password}</div>
                    )}

                    {!isLogin && (
                      <ul className="password-checklist" aria-live="polite">
                        <li className={passwordChecks.minLength ? "is-met" : ""}>
                          At least 8 characters
                        </li>
                        <li className={passwordChecks.hasUpper ? "is-met" : ""}>
                          One uppercase letter
                        </li>
                        <li className={passwordChecks.hasLower ? "is-met" : ""}>
                          One lowercase letter
                        </li>
                        <li className={passwordChecks.hasNumber ? "is-met" : ""}>
                          One number
                        </li>
                        <li className={passwordChecks.hasSpecial ? "is-met" : ""}>
                          One special character
                        </li>
                      </ul>
                    )}
                  </div>

                  {successMessage && (
                    <div className="alert alert-success py-2 mb-3" role="status">
                      {successMessage}
                    </div>
                  )}

                  {error && (
                    <div className="alert alert-danger py-2 mb-3" role="alert">
                      {error}
                    </div>
                  )}

                  {isLogin && (
                    <div className="text-end mb-3">
                      <a href="/forgot-password" className="small text-primary text-decoration-none">
                        Forgot password?
                      </a>
                    </div>
                  )}

                  <button
                    type="submit"
                    className="btn btn-primary w-100 text-nav"
                    disabled={isAuthBusy}
                  >
                    {loading
                      ? "Processing..."
                      : isLogin
                        ? "Login to Dashboard"
                        : "Create Account"}
                  </button>

                  <div className="auth-divider" role="separator" aria-label="or">
                    <span>or</span>
                  </div>

                  <GoogleSignInButton
                    disabled={isAuthBusy}
                    onStart={() => {
                      setError("");
                      setSuccessMessage("");
                      clearFieldErrors();
                      setGoogleLoading(true);
                    }}
                    onSuccess={(user) => {
                      setGoogleLoading(false);
                      handleGoogleSignIn(user);
                    }}
                    onError={(message) => {
                      setGoogleLoading(false);
                      setError(message);
                    }}
                  />

                  {googleLoading && (
                    <p className="text-center text-body-secondary small mt-2 mb-0">
                      Connecting to Google...
                    </p>
                  )}

                  <p className="text-center text-body-secondary small mt-3 mb-0">
                    {isLogin
                      ? "Don't have an account?"
                      : "Already have an account?"}{" "}
                    <button
                      type="button"
                      className="btn btn-link p-0 align-baseline text-nav text-primary text-decoration-none"
                      onClick={() => switchAuthMode(!isLogin)}
                    >
                      {isLogin ? "Sign up" : "Login"}
                    </button>
                  </p>
                </form>

                <div className="auth-privacy mt-4">
                  🔒 Your data is minimized and protected before AI processing.
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="landing-features py-5" id="features">
        <div className="container">
          <div className="text-center mb-5">
            <h2 className="landing-features__title mb-2">
              Everything you need to budget smarter
            </h2>
            <p className="text-body-brand text-body-secondary">
              Simple tools for clear financial decisions.
            </p>
          </div>

          <div className="row row-cols-1 row-cols-md-2 row-cols-lg-3 g-4">
            {features.map((f) => (
              <div className="col" key={f.title}>
                <div className="card glass-card h-100">
                  <div className="card-body">
                    <div className="feature-card__icon mb-3">{f.icon}</div>
                    <h4 className="h6 fw-bold mb-2">{f.title}</h4>
                    <p className="text-body-secondary small mb-0">{f.desc}</p>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      <footer className="landing-footer glass-panel glass-panel--footer">
        <div className="container py-5">
          <div className="row g-4 mb-4">
            <div className="col-md-4">
              <div className="mb-3">
                <BrandName logoSize="md" />
              </div>
              <p className="landing-footer__text small mb-0">
                Private AI-powered budgeting for modern financial clarity.
              </p>
            </div>

            <div className="col-md-4">
              <h6 className="text-label landing-footer__heading mb-3">Product</h6>
              <ul className="list-unstyled landing-footer__links mb-0">
                <li>Dashboard</li>
                <li>Insights</li>
                <li>Reports</li>
              </ul>
            </div>

            <div className="col-md-4">
              <h6 className="text-label landing-footer__heading mb-3">Company</h6>
              <ul className="list-unstyled landing-footer__links mb-0">
                <li>About</li>
                <li>Privacy</li>
                <li>Security</li>
              </ul>
            </div>
          </div>

          <hr className="landing-footer__hr" />

          <p className="text-center landing-footer__copy small mb-0">
            © {new Date().getFullYear()} {BRAND_NAME}. All rights reserved.
          </p>
        </div>
      </footer>
    </div>
  );
};

export default Landing;
