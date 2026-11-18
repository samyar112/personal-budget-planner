import { useEffect, useState } from "react";
import { ApiError } from "../api/auth";
import { fetchPlaidItems, syncPlaidItem, type PlaidItem } from "../api/plaid";
import ConnectBankButton from "./ConnectBankButton";

const CARD_STYLES = ["payment-card--navy", "payment-card--orange", "payment-card--green"] as const;
const ACCOUNTS_SHOWN = 3;

/** "just now", "5 min ago", "3 h ago", "2 days ago". Coarse on purpose: it is a freshness hint. */
function syncedAgo(iso: string | null): string {
  if (!iso) return "Never synced";
  const minutes = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
  if (minutes < 1) return "Synced just now";
  if (minutes < 60) return `Synced ${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `Synced ${hours} h ago`;
  const days = Math.floor(hours / 24);
  return `Synced ${days} ${days === 1 ? "day" : "days"} ago`;
}

/** The bank's non-card accounts as one line; cards are drawn separately inside the bank panel. */
function accountSummary(item: PlaidItem): string {
  const names = item.accounts
    .filter((account) => account.type !== "credit")
    .map((account) => account.name.replace(/^Plaid /, ""));
  const shown = names.slice(0, ACCOUNTS_SHOWN).join(" · ");
  const more = names.length - ACCOUNTS_SHOWN;
  return more > 0 ? `${shown} · +${more} more` : shown;
}

type SyncState = { busy: boolean; message?: string; error?: boolean };

/**
 * One panel per linked bank: its accounts, Sync now, and its credit cards inside. Everything reads one
 * list from the API, so a link or a sync refreshes them together.
 */
const AccountsSection = ({ reloadKey, onChanged }: { reloadKey: number; onChanged: () => void }) => {
  const [items, setItems] = useState<
    { status: "loading" } | { status: "error"; message: string } | { status: "ready"; items: PlaidItem[] }
  >({ status: "loading" });
  const [sync, setSync] = useState<Record<string, SyncState>>({});
  const [attempt, setAttempt] = useState(0);
  // Banks start collapsed so the section stays one short row per bank; cards show on expand.
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  useEffect(() => {
    let cancelled = false;
    fetchPlaidItems()
      .then((result) => !cancelled && setItems({ status: "ready", items: result }))
      .catch(
        (err: unknown) =>
          !cancelled &&
          setItems({
            status: "error",
            message: err instanceof ApiError ? err.message : "Could not load your connected banks. Try again.",
          }),
      );
    return () => {
      cancelled = true;
    };
  }, [reloadKey, attempt]);

  const handleSync = async (id: string) => {
    setSync((current) => ({ ...current, [id]: { busy: true } }));
    try {
      const result = await syncPlaidItem(id);
      const changed = result.added + result.modified + result.removed;
      setSync((current) => ({
        ...current,
        [id]: {
          busy: false,
          message: changed === 0 ? "Up to date." : `${result.added} new, ${result.modified} updated, ${result.removed} removed.`,
        },
      }));
      setAttempt((value) => value + 1);
      onChanged();
    } catch (err) {
      setSync((current) => ({
        ...current,
        [id]: {
          busy: false,
          error: true,
          message: err instanceof ApiError ? err.message : "Could not sync this bank. Try again.",
        },
      }));
    }
  };

  const linked = items.status === "ready" ? items.items : [];

  return (
    <div className="card glass-card dashboard-card mb-4">
      <div className="card-body">
        <h4 className="mb-3">Your accounts</h4>

        {items.status === "loading" && (
          <p className="text-body-secondary mb-0" role="status">Loading your accounts…</p>
        )}

        {items.status === "error" && (
          <div className="alert alert-warning d-flex align-items-center gap-2 mb-0" role="alert">
            <span>{items.message}</span>
            <button
              type="button"
              className="btn btn-sm btn-outline-dark ms-auto"
              onClick={() => setAttempt((value) => value + 1)}
            >
              Try again
            </button>
          </div>
        )}

        {items.status === "ready" && (
          <>
            {linked.length === 0 && (
              <p className="text-body-secondary small">
                No banks connected yet. Connect one to see its accounts, cards and transactions here.
              </p>
            )}

            {linked.map((item) => {
              const state = sync[item.id];
              const cards = item.accounts.filter((account) => account.type === "credit");
              const others = accountSummary(item);
              const isOpen = expanded[item.id] ?? false;
              const panelId = `bank-panel-${item.id}`;
              return (
                <section className="bank-panel" key={item.id} aria-label={item.institution ?? "Linked bank"}>
                  <div className="bank-panel__header">
                    <button
                      type="button"
                      className="bank-panel__toggle"
                      aria-expanded={isOpen}
                      aria-controls={panelId}
                      onClick={() => setExpanded((current) => ({ ...current, [item.id]: !isOpen }))}
                    >
                      <span className="bank-panel__chevron" aria-hidden="true">{isOpen ? "▾" : "▸"}</span>
                      <span className="bank-row__name">🏦 {item.institution ?? "Linked bank"}</span>
                      <span className="bank-row__synced">
                        {cards.length} {cards.length === 1 ? "card" : "cards"} · {item.accounts.length - cards.length}{" "}
                        {item.accounts.length - cards.length === 1 ? "account" : "accounts"} · {syncedAgo(item.lastSyncedAt)}
                      </span>
                    </button>
                    <button
                      type="button"
                      className="btn btn-sm btn-secondary text-nav flex-shrink-0"
                      onClick={() => handleSync(item.id)}
                      disabled={state?.busy}
                    >
                      {state?.busy ? "Syncing…" : "Sync now"}
                    </button>
                  </div>

                  {state?.message && (
                    <div className={`small mt-1 ${state.error ? "text-danger" : "text-body-secondary"}`} role="status">
                      {state.message}
                    </div>
                  )}

                  {isOpen && (
                    <div id={panelId}>
                      {others && <div className="bank-row__accounts mt-2">{others}</div>}
                      {cards.length > 0 && (
                        <div className="row g-2 mt-1">
                          {cards.map((card, index) => (
                            <div className="col-sm-6 col-xl-4" key={card.id}>
                              <div className={`payment-card payment-card--compact ${CARD_STYLES[index % CARD_STYLES.length]}`}>
                                <div className="payment-card__left">
                                  <div className="payment-card__name">{card.name.replace(/^Plaid /, "")}</div>
                                  <div className="payment-card__num">**** **** **** {card.mask ?? "····"}</div>
                                </div>
                                <div className="payment-card__right">
                                  <div className="card-chip"></div>
                                </div>
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </section>
              );
            })}

            <div className="d-flex flex-wrap justify-content-center gap-2 mt-3">
              <ConnectBankButton
                label="+ Connect another bank"
                className="btn btn-success btn-sm text-nav"
                onLinked={onChanged}
              />
            </div>
          </>
        )}
      </div>
    </div>
  );
};

export default AccountsSection;
