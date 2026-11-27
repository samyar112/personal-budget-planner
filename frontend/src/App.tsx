import { lazy, Suspense, useEffect, useState, type ReactNode } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { fetchCurrentUser } from "./api/auth";
import { clearAuthSession } from "./components/BrandName";
import Landing from "./pages/Landing";
import Home from "./pages/Home";
import VerifyEmail from "./pages/VerifyEmail";
import ForgotPassword from "./pages/ForgotPassword";
import ResetPassword from "./pages/ResetPassword";
import AdminUsers from "./pages/AdminUsers";
import Accounts from "./pages/Accounts";
import Transactions from "./pages/Transactions";
import Statements from "./pages/Statements";
import Bills from "./pages/Bills";
import Insights from "./pages/Insights";
import Privacy from "./pages/Privacy";
import AppLayout from "./components/AppLayout";

// Recharts is most of the bundle and only this page uses it, so it loads when the page is opened.
const Spending = lazy(() => import("./pages/Spending"));

/**
 * Allows /home only when /api/auth/me succeeds (HttpOnly cookie). No client-side flag can grant
 * access. With `adminOnly`, also requires the Admin role. This only decides what to show: the API
 * enforces access itself.
 */
const RequireAuth = ({ children, adminOnly = false }: { children: ReactNode; adminOnly?: boolean }) => {
  const [status, setStatus] = useState<"loading" | "ok" | "deny" | "notAdmin">("loading");

  useEffect(() => {
    let cancelled = false;

    const verify = async () => {
      try {
        const me = await fetchCurrentUser();
        if (!cancelled) setStatus(adminOnly && me.role !== "Admin" ? "notAdmin" : "ok");
      } catch {
        clearAuthSession();
        if (!cancelled) setStatus("deny");
      }
    };

    void verify();
    return () => {
      cancelled = true;
    };
  }, [adminOnly]);

  if (status === "loading") {
    return (
      <div className="d-flex min-vh-100 align-items-center justify-content-center text-body-brand">
        Checking session…
      </div>
    );
  }

  if (status === "deny") {
    return <Navigate to="/" replace />;
  }

  if (status === "notAdmin") {
    return <Navigate to="/home" replace />;
  }

  return children;
};

const App = () => {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Landing />} />
        {/* Every signed-in page shares the sidebar layout; RequireAuth runs once for all of them. */}
        <Route
          element={
            <RequireAuth>
              <AppLayout />
            </RequireAuth>
          }
        >
          <Route path="/home" element={<Home />} />
          <Route path="/accounts" element={<Accounts />} />
          <Route path="/transactions" element={<Transactions />} />
          <Route path="/statements" element={<Statements />} />
          <Route path="/bills" element={<Bills />} />
          <Route
            path="/spending"
            element={
              <Suspense
                fallback={
                  <div className="text-center py-5" role="status">
                    <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" /> Loading…
                  </div>
                }
              >
                <Spending />
              </Suspense>
            }
          />
          <Route path="/insights" element={<Insights />} />
          <Route path="/privacy" element={<Privacy />} />
          <Route
            path="/admin/users"
            element={
              <RequireAuth adminOnly>
                <AdminUsers />
              </RequireAuth>
            }
          />
        </Route>
        <Route path="/verify-email" element={<VerifyEmail />} />
        <Route path="/forgot-password" element={<ForgotPassword />} />
        <Route path="/reset-password" element={<ResetPassword />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
};

export default App;
