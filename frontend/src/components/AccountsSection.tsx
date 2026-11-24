import { useEffect, useState } from "react";
import { ApiError } from "../api/auth";
import { fetchPlaidItems, syncPlaidItem, unlinkPlaidItem, type LinkedAccount, type PlaidItem } from "../api/plaid";
import { BRAND_NAME } from "./BrandName";
import ConnectBankButton from "./ConnectBankButton";

const CARD_STYLES = ["payment-card--navy", "payment-card--orange", "payment-card--green"] as const;
const TYPE_ORDER = ["depository", "credit", "loan", "investment"];
const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

/** "just now", "5 min ago", "3 h ago", "2 days ago". Coarse on purpose: it is a freshness hint. */
function ago(iso: string): string {
  const minutes = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.floor(hours / 24);
  return `${days} ${days === 1 ? "day" : "days"} ago`;
}

function syncedAgo(iso: string | null): string {
  return iso ? `Synced ${ago(iso)}` : "Never synced";
}

/** The tile's small label: Plaid's subtype when known ("Credit card"), else a plain word for the type. */
function kindLabel(account: LinkedAccount): string {
  const raw = account.subtype ?? (account.type === "depository" ? "account" : (account.type ?? "account"));
  return raw.charAt(0).toUpperCase() + raw.slice(1);
}

/** Credit and loan balances are what you owe, so the tile says so instead of reading as money you have. */
function balanceLabel(account: LinkedAccount): string {
  return account.type === "credit" || account.type === "loan" ? "Balance owed" : "Balance";
}

/** Depository first, then cards, loans and the rest; by name inside a type so the order never jumps. */
function sortedAccounts(item: PlaidItem): LinkedAccount[] {
  const rank = (account: LinkedAccount) => {
    const index = TYPE_ORDER.indexOf(account.type ?? "");
    return index === -1 ? TYPE_ORDER.length : index;
  };
  return [...item.accounts].sort((a, b) => rank(a) - rank(b) || a.name.localeCompare(b.name));
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
  // Unlink deletes data, so it asks first: the bank id being confirmed, and any error from the last try.
  const [confirmingUnlink, setConfirmingUnlink] = useState<string | null>(null);
  const [unlinking, setUnlinking] = useState<SyncState>({ busy: false });
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
      await syncPlaidItem(id);
      setSync((current) => ({ ...current, [id]: { busy: false, message: "Up to date." } }));
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

  const handleUnlink = async (id: string) => {
    setUnlinking({ busy: true });
    try {
      await unlinkPlaidItem(id);
      setConfirmingUnlink(null);
      setUnlinking({ busy: false });
      setAttempt((value) => value + 1);
      onChanged();
    } catch (err) {
      setUnlinking({
        busy: false,
        error: true,
        message: err instanceof ApiError ? err.message : "Could not unlink this bank. Nothing was deleted, so try again.",
      });
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
              <div className="text-center py-4 px-2">
                <h5 className="mb-2">Connect your first bank</h5>
                <p className="text-body-secondary mx-auto mb-3" style={{ maxWidth: "34rem" }}>
                  Link a bank or credit card to bring your accounts and transactions into one ledger.
                  Access is read-only, so {BRAND_NAME} can never move money, and identifying details are
                  stripped before anything reaches the AI.
                </p>
                <div className="d-flex flex-wrap justify-content-center gap-2">
                  <ConnectBankButton label="Connect a bank" onLinked={onChanged} />
                </div>
              </div>
            )}

            {linked.map((item) => {
              const state = sync[item.id];
              const accounts = sortedAccounts(item);
              // Only credit cards are drawn as cards. Everything else is a plain row with its balance.
              const rows = accounts.filter((account) => account.type !== "credit");
              const cards = accounts.filter((account) => account.type === "credit");
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
                        {accounts.length} {accounts.length === 1 ? "account" : "accounts"} · {syncedAgo(item.lastSyncedAt)}
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
                      {rows.length > 0 && (
                        <ul className="account-list mt-2">
                          {rows.map((account) => {
                            const kind = kindLabel(account);
                            const name = account.name.replace(/^Plaid /, "");
                            const detail = [name.toLowerCase() === kind.toLowerCase() ? null : kind, `•••• ${account.mask ?? "····"}`]
                              .filter(Boolean)
                              .join(" · ");
                            return (
                              <li className="account-row" key={account.id}>
                                <div className="account-row__main">
                                  <span className="account-row__name">{name}</span>
                                  <span className="account-row__meta">{detail}</span>
                                </div>
                                {account.currentBalance !== null && (
                                  <div className="account-row__balance">
                                    <span className="account-row__amount">{money.format(account.currentBalance)}</span>
                                    <span className="account-row__meta">
                                      {balanceLabel(account)}
                                      {account.balanceAsOf && ` · as of ${ago(account.balanceAsOf)}`}
                                    </span>
                                  </div>
                                )}
                              </li>
                            );
                          })}
                        </ul>
                      )}
                      {cards.length > 0 && (
                        <h6 className="bank-panel__heading mt-3 mb-2">{cards.length === 1 ? "Credit Card" : "Credit Cards"}</h6>
                      )}
                      {cards.length > 0 && (
                        <div className="row g-2">
                          {cards.map((card, index) => (
                            <div className="col-sm-6 col-xl-4" key={card.id}>
                              <div className={`payment-card payment-card--compact ${CARD_STYLES[index % CARD_STYLES.length]}`}>
                                <div className="payment-card__top">
                                  <div className="payment-card__name">{card.name.replace(/^Plaid /, "")}</div>
                                  <span className="card-chip" aria-hidden="true" />
                                </div>
                                <div className="payment-card__num">•••• {card.mask ?? "····"}</div>
                                {card.currentBalance !== null && (
                                  <div className="payment-card__balance">
                                    <span className="payment-card__balance-label">{balanceLabel(card)}</span>
                                    <span className="payment-card__balance-amount">{money.format(card.currentBalance)}</span>
                                    {card.balanceAsOf && (
                                      <span className="payment-card__balance-asof">as of {ago(card.balanceAsOf)}</span>
                                    )}
                                  </div>
                                )}
                              </div>
                            </div>
                          ))}
                        </div>
                      )}

                      <div className="bank-panel__footer">
                        {confirmingUnlink === item.id ? (
                          <div className="bank-panel__confirm" role="alertdialog" aria-label={`Unlink ${item.institution ?? "this bank"}`}>
                            <span className="small">
                              Unlink {item.institution ?? "this bank"}? This deletes its accounts and all of its transactions from{" "}
                              {BRAND_NAME}.
                            </span>
                            {unlinking.error && <span className="small text-danger" role="alert">{unlinking.message}</span>}
                            <div className="d-flex gap-2">
                              <button
                                type="button"
                                className="btn btn-sm btn-outline-secondary"
                                onClick={() => {
                                  setConfirmingUnlink(null);
                                  setUnlinking({ busy: false });
                                }}
                                disabled={unlinking.busy}
                              >
                                Cancel
                              </button>
                              <button
                                type="button"
                                className="btn btn-sm btn-danger"
                                onClick={() => handleUnlink(item.id)}
                                disabled={unlinking.busy}
                              >
                                {unlinking.busy ? "Unlinking…" : "Yes, unlink"}
                              </button>
                            </div>
                          </div>
                        ) : (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-danger"
                            onClick={() => {
                              setConfirmingUnlink(item.id);
                              setUnlinking({ busy: false });
                            }}
                          >
                            Unlink bank
                          </button>
                        )}
                      </div>
                    </div>
                  )}
                </section>
              );
            })}

            {linked.length > 0 && (
              <div className="d-flex flex-wrap justify-content-center gap-2 mt-3">
                <ConnectBankButton
                  label="+ Connect another bank"
                  className="btn btn-success btn-sm text-nav"
                  onLinked={onChanged}
                />
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
};

export default AccountsSection;
