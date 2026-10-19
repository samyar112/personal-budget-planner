import { useEffect, useState, type ReactNode } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { fetchCurrentUser } from "./api/auth";
import { AUTH_GOOGLE_STUB_KEY, clearAuthSession } from "./components/BrandName";
import Landing from "./pages/Landing";
import Home from "./pages/Home";
import VerifyEmail from "./pages/VerifyEmail";
import ForgotPassword from "./pages/ForgotPassword";
import ResetPassword from "./pages/ResetPassword";
import AdminUsers from "./pages/AdminUsers";

/**
 * Allows /home only when /api/auth/me succeeds (HttpOnly cookie),
 * or when the temporary Google stub flag is set. With `adminOnly`, also requires the Admin role
 * and never honours the stub. This only decides what to show: the API enforces admin access itself.
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
        // Dev-only: lets the client-side Google stub reach /home. It is a localStorage flag anyone can
        // set, so it must never grant access in a production build (Vite strips this branch there).
        if (!adminOnly && import.meta.env.DEV && localStorage.getItem(AUTH_GOOGLE_STUB_KEY) === "true") {
          if (!cancelled) setStatus("ok");
          return;
        }

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
        <Route
          path="/home"
          element={
            <RequireAuth>
              <Home />
            </RequireAuth>
          }
        />
        <Route
          path="/admin/users"
          element={
            <RequireAuth adminOnly>
              <AdminUsers />
            </RequireAuth>
          }
        />
        <Route path="/verify-email" element={<VerifyEmail />} />
        <Route path="/forgot-password" element={<ForgotPassword />} />
        <Route path="/reset-password" element={<ResetPassword />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
};

export default App;
