import { useEffect, useState } from "react";
import { ApiError } from "../../api/auth";

/** A brand colour token from :root, read at render so the charts follow the stylesheet. */
export function cssVar(name: string, fallback: string): string {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}

/** "$1.2k" style axis ticks; tooltips use the full amount. */
const compact = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", notation: "compact" });
export function formatCompact(amount: number): string {
  return compact.format(amount);
}

export type Load<T> = { status: "loading" } | { status: "ready"; data: T } | { status: "error"; message: string };

type Settled<T> = { key: string; result: Exclude<Load<T>, { status: "loading" }> };

/**
 * Loads one chart's data whenever `key` changes. Loading is derived, not stored: the chart is loading
 * while the last settled response belongs to an older key, so a stale response can never show.
 * `key` is a string so callers pass exactly the inputs the chart depends on.
 */
export function useChartData<T>(key: string, load: () => Promise<T>): [Load<T>, () => void] {
  const [attempt, setAttempt] = useState(0);
  const [settled, setSettled] = useState<Settled<T> | null>(null);
  const current = `${attempt}|${key}`;

  useEffect(() => {
    let cancelled = false;
    load()
      .then((data) => !cancelled && setSettled({ key: current, result: { status: "ready", data } }))
      .catch((err: unknown) => {
        if (!cancelled) {
          const message = err instanceof ApiError ? err.message : "Could not load this chart. Try again.";
          setSettled({ key: current, result: { status: "error", message } });
        }
      });
    return () => {
      cancelled = true;
    };
    // `load` is rebuilt every render; `current` carries its inputs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [current]);

  const state: Load<T> = settled?.key === current ? settled.result : { status: "loading" };
  return [state, () => setAttempt((value) => value + 1)];
}
