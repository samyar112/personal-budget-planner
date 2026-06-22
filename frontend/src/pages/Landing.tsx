import { useState } from "react";
import { useNavigate } from "react-router-dom";
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
  const [error, setError] = useState<string>("");

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

  const handleSubmit: React.FormEventHandler<HTMLFormElement> = async (e) => {
    e.preventDefault();
    setError("");

    if (!validate()) return;

    setLoading(true);

    try {
      // Simulated auth request
      await new Promise((res) => setTimeout(res, 800));

      localStorage.setItem("finguard-auth", "true");

      navigate("/home");
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="landing-page">

      {/* NAV */}
      <nav className="landing-nav">
        <div className="landing-nav__brand">FinGuard</div>
        <div className="landing-nav__links">
          <a href="#features">Features</a>
          <a href="#pricing">Pricing</a>
          <a href="#about">About</a>
        </div>
      </nav>

      {/* HERO */}
      <section className="landing-hero">

        {/* LEFT */}
        <div className="landing-hero__left">
          <span className="landing-eyebrow">AI-Powered Budgeting</span>

          <h1 className="landing-headline">
            Your money, <br />
            <span className="landing-headline--accent">
              finally clear.
            </span>
          </h1>

          <p className="landing-subhead">
            FinGuard helps you understand spending, reduce waste,
            and build better financial habits—securely and privately.
          </p>

          <div className="landing-stats">
            <div className="landing-stat">
              <span className="landing-stat__num">10k+</span>
              <span className="landing-stat__label">Users</span>
            </div>

            <div className="landing-stat__divider" />

            <div className="landing-stat">
              <span className="landing-stat__num">$2.4M</span>
              <span className="landing-stat__label">Saved</span>
            </div>

            <div className="landing-stat__divider" />

            <div className="landing-stat">
              <span className="landing-stat__num">98%</span>
              <span className="landing-stat__label">Privacy-first</span>
            </div>
          </div>
        </div>

        {/* RIGHT AUTH CARD */}
        <div className="landing-hero__right">

          <div className="auth-card">

            {/* Tabs */}
            <div className="auth-card__tabs">
              <button
                type="button"
                className={`auth-tab ${isLogin ? "auth-tab--active" : ""}`}
                onClick={() => {
                  setIsLogin(true);
                  setError("");
                }}
              >
                Login
              </button>

              <button
                type="button"
                className={`auth-tab ${!isLogin ? "auth-tab--active" : ""}`}
                onClick={() => {
                  setIsLogin(false);
                  setError("");
                }}
              >
                Sign Up
              </button>
            </div>

            {/* FORM */}
            <form className="auth-form" onSubmit={handleSubmit}>

              {!isLogin && (
                <div className="auth-field">
                  <label htmlFor="name">Full Name</label>
                  <input
                    id="name"
                    name="name"
                    type="text"
                    placeholder="Jane Smith"
                    value={form.name}
                    onChange={handleChange}
                  />
                </div>
              )}

              <div className="auth-field">
                <label htmlFor="email">Email</label>
                <input
                  id="email"
                  name="email"
                  type="email"
                  placeholder="you@example.com"
                  value={form.email}
                  onChange={handleChange}
                />
              </div>

              <div className="auth-field">
                <label htmlFor="password">Password</label>
                <input
                  id="password"
                  name="password"
                  type="password"
                  placeholder="••••••••"
                  value={form.password}
                  onChange={handleChange}
                />
              </div>

              {error && <p className="auth-error">{error}</p>}

              {isLogin && (
                <div className="auth-forgot">
                  <a href="/forgot-password">Forgot password?</a>
                </div>
              )}

              <button
                type="submit"
                className="auth-submit"
                disabled={loading}
              >
                {loading
                  ? "Processing..."
                  : isLogin
                  ? "Login to Dashboard"
                  : "Create Account"}
              </button>

              <p className="auth-switch">
                {isLogin
                  ? "Don't have an account?"
                  : "Already have an account?"}{" "}
                <span
                  onClick={() => {
                    setIsLogin(!isLogin);
                    setError("");
                  }}
                >
                  {isLogin ? "Sign up" : "Login"}
                </span>
              </p>
            </form>

            <div className="auth-privacy">
              🔒 Your data is minimized and protected before AI processing.
            </div>
          </div>
        </div>
      </section>

      {/* FEATURES */}
      <section className="landing-features" id="features">
        <div className="landing-features__header">
          <h2>Everything you need to budget smarter</h2>
          <p>Simple tools for clear financial decisions.</p>
        </div>

        <div className="features-grid">
          {features.map((f, i) => (
            <div className="feature-card" key={i}>
              <div className="feature-card__icon">{f.icon}</div>
              <h4>{f.title}</h4>
              <p>{f.desc}</p>
            </div>
          ))}
        </div>
      </section>

      {/* FOOTER */}
      <footer className="landing-footer">
        <div className="landing-footer__inner">

          <div>
            <h5>FinGuard</h5>
            <p>
              Private AI-powered budgeting for modern financial clarity.
            </p>
          </div>

          <div>
            <h6>Product</h6>
            <ul>
              <li>Dashboard</li>
              <li>Insights</li>
              <li>Reports</li>
            </ul>
          </div>

          <div>
            <h6>Company</h6>
            <ul>
              <li>About</li>
              <li>Privacy</li>
              <li>Security</li>
            </ul>
          </div>
        </div>

        <div className="landing-footer__copy">
          © {new Date().getFullYear()} FinGuard. All rights reserved.
        </div>
      </footer>

    </div>
  );
};

export default Landing;