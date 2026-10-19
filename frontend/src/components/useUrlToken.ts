import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";

/**
 * Reads the emailed ?token= once, then removes it from the address bar. A reset or verify token
 * left in the URL ends up in browser history, screenshots and any Referer header; the page keeps
 * its own copy in state.
 */
export const useUrlToken = (): string => {
  const location = useLocation();
  const navigate = useNavigate();
  const [token] = useState(() => new URLSearchParams(location.search).get("token") ?? "");

  useEffect(() => {
    if (new URLSearchParams(location.search).has("token")) {
      navigate(location.pathname, { replace: true });
    }
  }, [location.pathname, location.search, navigate]);

  return token;
};
