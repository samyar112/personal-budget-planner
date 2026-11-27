import { useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { checkIdleSession, fetchCurrentUser, logoutUser } from "../api/auth";
import { clearAuthSession } from "./BrandName";

/** While the user is active, tell the server at most this often. The server bumps at most once a minute anyway. */
const PING_INTERVAL_MS = 5 * 60 * 1000;
/** How often to compare the clock with the deadline. Local only: no request unless the deadline has passed. */
const TICK_MS = 15 * 1000;
const ACTIVITY_EVENTS = ["pointerdown", "keydown", "wheel", "touchstart", "scroll"] as const;

export const IDLE_SIGN_OUT_NOTICE = "Signed out after 15 minutes of inactivity.";

const deadlineOf = (idleExpiresAt: string | null | undefined) =>
  idleExpiresAt ? Date.parse(idleExpiresAt) : null;

/**
 * Signs this tab out once the server says the login has been idle for its window. The server is the only
 * clock: this hook keeps no activity state in storage. Input pings the server now and then; at the local
 * deadline the tab asks the server, and activity in any other tab or window shows up as a later deadline.
 * Timers pause while a laptop sleeps, so every check compares timestamps rather than counting ticks.
 */
const useIdleSignOut = () => {
  const navigate = useNavigate();
  const deadline = useRef<number | null>(null);
  const lastPing = useRef(0);
  const busy = useRef(false);

  useEffect(() => {
    let stopped = false;

    const signOut = async () => {
      stopped = true;
      try {
        await logoutUser();
      } catch {
        // The server has usually ended the session already; clearing the client is what matters here.
      }
      clearAuthSession();
      navigate("/", { replace: true, state: { notice: IDLE_SIGN_OUT_NOTICE } });
    };

    const ping = async () => {
      lastPing.current = Date.now();
      try {
        deadline.current = deadlineOf((await fetchCurrentUser()).idleExpiresAt) ?? deadline.current;
      } catch {
        // A failed ping is not proof of anything; the deadline check decides.
      }
    };

    const check = async () => {
      if (stopped || busy.current || deadline.current === null || Date.now() < deadline.current) return;
      busy.current = true;
      try {
        const me = await checkIdleSession();
        const next = deadlineOf(me?.idleExpiresAt);
        // Comparing the server's deadline with the server's earlier one ignores any skew in this clock.
        if (next !== null && next > deadline.current) {
          deadline.current = next;
        } else if (!stopped) {
          await signOut();
        }
      } finally {
        busy.current = false;
      }
    };

    const onActivity = () => {
      if (Date.now() - lastPing.current >= PING_INTERVAL_MS) void ping();
    };

    const onVisible = () => {
      if (document.visibilityState === "visible") void check();
    };

    void ping();
    const timer = window.setInterval(() => void check(), TICK_MS);
    for (const name of ACTIVITY_EVENTS) window.addEventListener(name, onActivity, { passive: true });
    document.addEventListener("visibilitychange", onVisible);

    return () => {
      stopped = true;
      window.clearInterval(timer);
      for (const name of ACTIVITY_EVENTS) window.removeEventListener(name, onActivity);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [navigate]);
};

export default useIdleSignOut;
