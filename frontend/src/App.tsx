import { useEffect, useState, type ReactNode } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { fetchCurrentUser } from "./api/auth";
import { AUTH_GOOGLE_STUB_KEY, clearAuthSession } from "./components/BrandName";
import Landing from "./pages/Landing";
import Home from "./pages/Home";

/**
 * Allows /home only when /api/auth/me succeeds (HttpOnly cookie),
 * or when the temporary Google stub flag is set.
 */
const RequireAuth = ({ children }: { children: ReactNode }) => {
  const [status, setStatus] = useState<"loading" | "ok" | "deny">("loading");

  useEffect(() => {
    let cancelled = false;

    const verify = async () => {
      try {
        await fetchCurrentUser();
        if (!cancelled) setStatus("ok");
      } catch {
        if (localStorage.getItem(AUTH_GOOGLE_STUB_KEY) === "true") {
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
  }, []);

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
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
};

export default App;
