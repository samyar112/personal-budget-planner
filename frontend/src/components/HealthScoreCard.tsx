import { useEffect, useState } from "react";
import { ApiError } from "../api/auth";
import { fetchHealthScore, type HealthIndicator, type HealthScore } from "../api/transactions";

const BADGE = { Healthy: "badge-good", Coping: "badge-fair", Vulnerable: "badge-poor" } as const;
const FILL = { Healthy: "is-good", Coping: "is-fair", Vulnerable: "is-poor" } as const;

const tierOf = (score: number) => (score >= 80 ? "Healthy" : score >= 40 ? "Coping" : "Vulnerable");

/**
 * The financial health score: the total and tier, each of the four components with its two indicators, and
 * a legend that explains every rule. The rule text comes from the server with the numbers, so the legend
 * always describes the arithmetic that produced them.
 */
const HealthScoreCard = ({ reloadKey }: { reloadKey: number }) => {
  const [state, setState] = useState<
    { status: "loading" } | { status: "error"; message: string } | { status: "ready"; health: HealthScore }
  >({ status: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setState({ status: "loading" });
      try {
        const health = await fetchHealthScore();
        if (!cancelled) setState({ status: "ready", health });
      } catch (err) {
        if (!cancelled) {
          setState({
            status: "error",
            message: err instanceof ApiError ? err.message : "Could not load your health score. Try again.",
          });
        }
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [reloadKey, attempt]);

  return (
    <section className="summary-card score health-card mb-4" aria-label="Financial health score">
      <div className="health-card__head">
        <div>
          <h6>Financial health</h6>
          {state.status === "ready" ? (
            <Total health={state.health} />
          ) : (
            <h3 aria-busy={state.status === "loading"}>
              —<span className="score-max">/100</span>
            </h3>
          )}
        </div>
        {state.status === "error" && (
          <div className="d-flex align-items-center gap-2">
            <span className="score-label">{state.message}</span>
            <button type="button" className="btn btn-sm btn-outline-dark" onClick={() => setAttempt((n) => n + 1)}>
              Try again
            </button>
          </div>
        )}
      </div>

      {state.status === "ready" && (
        <>
          <div className="row g-3 mt-1">
            {state.health.components.map((component) => (
              <div className="col-md-6 col-xl-3" key={component.id}>
                <div className="health-component">
                  <div className="health-component__head">
                    <span>{component.name}</span>
                    <span>{component.score ?? "—"}</span>
                  </div>
                  <div
                    className="health-bar"
                    role="meter"
                    aria-label={`${component.name} score`}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-valuenow={component.score ?? 0}
                  >
                    <span
                      className={`health-bar__fill ${component.score === null ? "" : FILL[tierOf(component.score)]}`}
                      style={{ width: `${component.score ?? 0}%` }}
                    />
                  </div>
                  <ul className="health-indicators">
                    {component.indicators.map((indicator) => (
                      <Indicator key={indicator.id} indicator={indicator} />
                    ))}
                  </ul>
                </div>
              </div>
            ))}
          </div>
          <Legend health={state.health} />
        </>
      )}
    </section>
  );
};

const Total = ({ health }: { health: HealthScore }) => (
  <>
    <h3>
      {health.score ?? "—"}
      <span className="score-max">/100</span>
      {health.tier && <span className={`score-badge ms-2 align-middle ${BADGE[health.tier]}`}>{health.tier}</span>}
    </h3>
    <p className="score-label">
      {health.score === null
        ? "Not enough data yet: the score needs income in at least one complete month."
        : `Scored on ${health.indicatorsScored} of ${health.indicatorsTotal} indicators, over ${health.monthsComplete} complete ${health.monthsComplete === 1 ? "month" : "months"}.`}
    </p>
  </>
);

const Indicator = ({ indicator }: { indicator: HealthIndicator }) => (
  <li className={indicator.score === null ? "health-indicator--missing" : undefined}>
    <div className="health-indicator__head">
      <span>
        {indicator.name}
        {indicator.estimated && <sup title="Estimated from your transactions">*</sup>}
      </span>
      <span>{indicator.score ?? "no data"}</span>
    </div>
    <p className="health-indicator__reason">{indicator.reason}</p>
  </li>
);

const Legend = ({ health }: { health: HealthScore }) => (
  <details className="health-legend">
    <summary>How this is calculated</summary>
    <p>
      Modelled on the Financial Health Network&apos;s FinHealth framework: four components, two indicators each,
      every indicator scored 0–100. Your score is the average of the indicators we have data for, so a missing
      account changes what is counted, not the score itself. <strong>Healthy</strong> 80–100,{" "}
      <strong>Coping</strong> 40–79, <strong>Vulnerable</strong> 0–39. Income and spending are averaged over up
      to 6 complete months. Computed here from your own data; none of it is sent to an AI.
    </p>
    <dl>
      {health.components.flatMap((component) =>
        component.indicators.map((indicator) => (
          <div key={indicator.id}>
            <dt>
              {component.name} · {indicator.name}
              {indicator.estimated && <sup>*</sup>}
            </dt>
            <dd>{indicator.method}</dd>
          </div>
        )),
      )}
    </dl>
    <p className="mb-0">
      <sup>*</sup> Estimated: the framework asks these in a survey, so we infer them from your transactions and
      accounts.
    </p>
  </details>
);

export default HealthScoreCard;
