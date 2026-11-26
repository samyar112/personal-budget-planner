import type { LinkedAccount, PlaidItem } from "../api/plaid";

const TYPE_ORDER = ["depository", "credit", "loan", "investment"];
export const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

/** "just now", "5 min ago", "3 h ago", "2 days ago". Coarse on purpose: it is a freshness hint. */
export function ago(iso: string): string {
  const minutes = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.floor(hours / 24);
  return `${days} ${days === 1 ? "day" : "days"} ago`;
}

/** The tile's small label: Plaid's subtype when known ("Credit card"), else a plain word for the type. */
export function kindLabel(account: LinkedAccount): string {
  const raw = account.subtype ?? (account.type === "depository" ? "account" : (account.type ?? "account"));
  return raw.charAt(0).toUpperCase() + raw.slice(1);
}

/** Credit and loan balances are what you owe, so the tile says so instead of reading as money you have. */
export function balanceLabel(account: LinkedAccount): string {
  return account.type === "credit" || account.type === "loan" ? "Balance owed" : "Balance";
}

/** Depository first, then cards, loans and the rest; by name inside a type so the order never jumps. */
export function sortedAccounts(item: PlaidItem): LinkedAccount[] {
  const rank = (account: LinkedAccount) => {
    const index = TYPE_ORDER.indexOf(account.type ?? "");
    return index === -1 ? TYPE_ORDER.length : index;
  };
  return [...item.accounts].sort((a, b) => rank(a) - rank(b) || a.name.localeCompare(b.name));
}

