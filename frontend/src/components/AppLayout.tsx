import { useEffect, useState } from "react";
import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { ApiError, fetchCurrentUser, logoutUser, resendVerification } from "../api/auth";
import AmbientBackground from "./AmbientBackground";
import BrandLogo from "./BrandLogo";
import BrandName, { clearAuthSession } from "./BrandName";
import NavIcon, { type NavIconName } from "./NavIcon";
import "../pages/Home.css";
import "./AppLayout.css";

type NavItem = { to: string; label: string; icon: NavIconName };
type NavGroup = { title: string; items: NavItem[]; adminOnly?: boolean };

const NAV_GROUPS: NavGroup[] = [
  {
    title: "Overview",
    items: [
      { to: "/home", label: "Dashboard", icon: "dashboard" },
      { to: "/accounts", label: "Accounts", icon: "accounts" },
      { to: "/transactions", label: "Transactions", icon: "transactions" },
    ],
  },
  { title: "Planning", items: [{ to: "/bills", label: "Bills", icon: "bills" }] },
  {
    title: "Insights",
    items: [
      { to: "/spending", label: "Spending", icon: "spending" },
      { to: "/insights", label: "Insights", icon: "insights" },
      { to: "/privacy", label: "Privacy", icon: "privacy" },
    ],
  },
  { title: "Admin", adminOnly: true, items: [{ to: "/admin/users", label: "Users", icon: "users" }] },
];

const COLLAPSED_KEY = "vaultbudget-sidebar-collapsed";

// A UI preference only; storage can be blocked (private mode), so failures fall back to expanded.
const readCollapsed = () => {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === "true";
  } catch {
    return false;
  }
};

/**
 * Shell for every signed-in page: the sidebar (an icon rail when collapsed on desktop, a drawer on
 * phones), a top bar with the page title, and the verify-email banner. Pages render in the Outlet.
 */
const AppLayout = () => {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [isAdmin, setIsAdmin] = useState(false);
  const [unverifiedEmail, setUnverifiedEmail] = useState<string | null>(null);
  const [resendStatus, setResendStatus] = useState("");
  const [trialDaysRemaining, setTrialDaysRemaining] = useState<number | null>(null);

  useEffect(() => {
    fetchCurrentUser()
      .then((me) => {
        setIsAdmin(me.role === "Admin");
        setUnverifiedEmail(me.emailVerified ? null : me.email);
        setTrialDaysRemaining(me.subscribed ? null : me.trialDaysRemaining);
      })
      .catch(() => setUnverifiedEmail(null));
  }, []);

  useEffect(() => {
    if (!drawerOpen) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setDrawerOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [drawerOpen]);

  const toggleCollapsed = () => {
    setCollapsed((current) => {
      try {
        localStorage.setItem(COLLAPSED_KEY, String(!current));
      } catch {
        // Preference just won't persist.
      }
      return !current;
    });
  };

  const handleResend = async () => {
    if (!unverifiedEmail) return;
    setResendStatus("");
    try {
      await resendVerification(unverifiedEmail);
      setResendStatus("Verification email sent. Check your inbox.");
    } catch (err) {
      setResendStatus(err instanceof ApiError ? err.message : "Could not send the email. Try again.");
    }
  };

  const handleLogout = async () => {
    try {
      await logoutUser();
    } catch {
      // Still clear client state if the network call fails.
    }
    clearAuthSession();
    navigate("/");
  };

  const groups = NAV_GROUPS.filter((group) => !group.adminOnly || isAdmin);
  const pageTitle =
    NAV_GROUPS.flatMap((group) => group.items).find((item) => pathname.startsWith(item.to))?.label ?? "";

  return (
    <div className={`app-shell ${collapsed ? "app-shell--collapsed" : ""}`}>
      <AmbientBackground />

      <aside
        id="app-sidebar"
        className={`app-sidebar glass-panel ${drawerOpen ? "app-sidebar--open" : ""}`}
        aria-label="Main navigation"
      >
        <div className="app-sidebar__header">
          <Link to="/home" className="app-sidebar__brand" onClick={() => setDrawerOpen(false)}>
            {collapsed ? <BrandLogo size="sm" /> : <BrandName logoSize="sm" />}
          </Link>
          <button
            type="button"
            className="app-sidebar__icon-btn d-none d-lg-inline-flex"
            onClick={toggleCollapsed}
            aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
            aria-expanded={!collapsed}
            title={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          >
            <NavIcon name="collapse" />
          </button>
          <button
            type="button"
            className="app-sidebar__icon-btn d-lg-none"
            onClick={() => setDrawerOpen(false)}
            aria-label="Close menu"
          >
            <NavIcon name="close" />
          </button>
        </div>

        <nav className="app-sidebar__nav">
          {groups.map((group) => (
            <div className="app-sidebar__group" key={group.title}>
              <div className="app-sidebar__group-title">{group.title}</div>
              <ul className="list-unstyled mb-0">
                {group.items.map((item) => (
                  <li key={item.to}>
                    <NavLink
                      to={item.to}
                      className={({ isActive }) => `app-sidebar__link ${isActive ? "active" : ""}`}
                      onClick={() => setDrawerOpen(false)}
                      title={collapsed ? item.label : undefined}
                    >
                      <NavIcon name={item.icon} />
                      <span className="app-sidebar__label">{item.label}</span>
                    </NavLink>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>

        <div className="app-sidebar__footer">
          <button
            type="button"
            className="app-sidebar__link w-100"
            onClick={handleLogout}
            title={collapsed ? "Log out" : undefined}
          >
            <NavIcon name="logout" />
            <span className="app-sidebar__label">Log out</span>
          </button>
        </div>
      </aside>

      {drawerOpen && <div className="app-sidebar-backdrop d-lg-none" onClick={() => setDrawerOpen(false)} />}

      <div className="app-main">
        <header className="app-topbar">
          <button
            type="button"
            className="app-sidebar__icon-btn d-lg-none"
            onClick={() => setDrawerOpen(true)}
            aria-label="Open menu"
            aria-controls="app-sidebar"
            aria-expanded={drawerOpen}
          >
            <NavIcon name="menu" />
          </button>
          <h1 className="app-topbar__title">{pageTitle}</h1>
        </header>

        <main className="app-content">
          {unverifiedEmail && (
            <div className="alert alert-warning d-flex flex-wrap align-items-center gap-2" role="status">
              <span>
                Verify <strong>{unverifiedEmail}</strong> to unlock your finances. Check your inbox for the link.
              </span>
              <button type="button" className="btn btn-sm btn-outline-dark ms-auto" onClick={handleResend}>
                Resend email
              </button>
              {resendStatus && <span className="w-100 small">{resendStatus}</span>}
            </div>
          )}

          {trialDaysRemaining !== null && (
            <div
              className={`alert ${trialDaysRemaining > 0 ? "alert-info" : "alert-warning"} d-flex flex-wrap align-items-center gap-2`}
              role="status"
            >
              <span>
                {trialDaysRemaining > 0
                  ? `${trialDaysRemaining} ${trialDaysRemaining === 1 ? "day" : "days"} left in your free trial.`
                  : "Your free trial has ended. AI recommendations are locked; everything else stays available."}
              </span>
              <Link to="/insights" className="btn btn-sm btn-outline-dark ms-auto">
                {trialDaysRemaining > 0 ? "Upgrade for $2.99/month" : "Upgrade"}
              </Link>
            </div>
          )}

          <Outlet />
        </main>
      </div>
    </div>
  );
};

export default AppLayout;
