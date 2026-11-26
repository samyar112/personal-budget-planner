import type { ReactNode } from "react";
import type { Load } from "./chartSupport";

/** Title, optional subtitle and the loading, error and empty states every chart shares. */
export default function ChartCard<T>({
  title,
  subtitle,
  state,
  retry,
  isEmpty,
  emptyText,
  children,
}: {
  title: string;
  subtitle?: ReactNode;
  state: Load<T>;
  retry: () => void;
  isEmpty: (data: T) => boolean;
  emptyText: string;
  children: (data: T) => ReactNode;
}) {
  return (
    <div className="card glass-card dashboard-card spending-chart">
      <div className="card-body">
        <div className="spending-chart__header">
          <h5 className="mb-0">{title}</h5>
          {subtitle && <span className="spending-chart__subtitle">{subtitle}</span>}
        </div>
        {state.status === "loading" && (
          <div className="spending-chart__state" role="status">
            <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" /> Loading…
          </div>
        )}
        {state.status === "error" && (
          <div className="spending-chart__state" role="alert">
            {state.message}
            <button type="button" className="btn btn-sm btn-outline-dark ms-3" onClick={retry}>
              Retry
            </button>
          </div>
        )}
        {state.status === "ready" &&
          (isEmpty(state.data) ? <div className="spending-chart__state">{emptyText}</div> : children(state.data))}
      </div>
    </div>
  );
}
