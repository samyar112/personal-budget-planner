import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import BrandName, {
  AUTH_STORAGE_KEY,
  AUTH_USER_STORAGE_KEY,
  BRAND_NAME,
} from "../components/BrandName";
import AmbientBackground from "../components/AmbientBackground";
import GoogleSignInButton from "../components/GoogleSignInButton";
import "./Landing.css";

type FormState = {
  name: string;
  email: string;
  password: string;
};

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

const Landing = () => {
  const navigate = useNavigate();

  const [isLogin, setIsLogin] = useState(true);
  const [loading, setLoading] = useState(false);
  const [googleLoading, setGoogleLoading] = useState(false);
  const [error, setError] = useState<string>("");
  const isAuthBusy = loading || googleLoading;

  const [form, setForm] = useState<FormState>({
    name: "",
    email: "",
    password: "",
  });

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setForm((prev) => ({
      ...prev,
      [e.target.name]: e.target.value,
    }));
  };

  const validate = () => {
    if (!form.email || !form.password) {
      setError("Email and password are required.");
      return false;
    }

    if (!/\S+@\S+\.\S+/.test(form.email)) {
      setError("Please enter a valid email address.");
      return false;
    }

    if (!isLogin && !form.name.trim()) {
      setError("Please enter your full name.");
      return false;
    }

    if (!isLogin && form.password.length < 6) {
      setError("Password must be at least 6 characters.");
      return false;
    }

    return true;
  };

  const completeSignIn = (user?: { name: string; email: string; picture?: string; provider: "email" | "google" }) => {
    localStorage.setItem(AUTH_STORAGE_KEY, "true");

    if (user) {
      localStorage.setItem(AUTH_USER_STORAGE_KEY, JSON.stringify(user));
    }

    navigate("/home");
  };

  const handleGoogleSignIn = (user: { name: string; email: string; picture?: string }) => {
    setError("");
    completeSignIn({ ...user, provider: "google" });
  };

  const handleSubmit: React.FormEventHandler<HTMLFormElement> = async (e) => {
    e.preventDefault();
    setError("");

    if (!validate()) return;

    setLoading(true);

    try {
      await new Promise((res) => setTimeout(res, 800));

      completeSignIn({
        name: form.name.trim() || form.email.split("@")[0],
        email: form.email,
        provider: "email",
      });
    } catch {
      setError("Something went wrong. Please try again.");
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
                      onClick={() => {
                        setIsLogin(true);
                        setError("");
                      }}
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
                      onClick={() => {
                        setIsLogin(false);
                        setError("");
                      }}
                    >
                      Sign Up
                    </button>
                  </li>
                </ul>

                <form onSubmit={handleSubmit}>
                  {!isLogin && (
                    <div className="mb-3">
                      <label htmlFor="name" className="form-label">
                        Full Name
                      </label>
                      <input
                        id="name"
                        name="name"
                        type="text"
                        className="form-control"
                        placeholder="Jane Smith"
                        value={form.name}
                        onChange={handleChange}
                      />
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
                      className="form-control"
                      placeholder="you@example.com"
                      value={form.email}
                      onChange={handleChange}
                    />
                  </div>

                  <div className="mb-3">
                    <label htmlFor="password" className="form-label">
                      Password
                    </label>
                    <input
                      id="password"
                      name="password"
                      type="password"
                      className="form-control"
                      placeholder="••••••••"
                      value={form.password}
                      onChange={handleChange}
                    />
                  </div>

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
                      onClick={() => {
                        setIsLogin(!isLogin);
                        setError("");
                      }}
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
