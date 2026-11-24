import { useEffect, useState } from "react";
import { fetchPlaidItems, type LinkedAccount, type PlaidItem } from "../api/plaid";
import type { AccountSummary } from "../api/transactions";
import { formatMoney } from "../api/transactions";
import { ago, balanceLabel, kindLabel, sortedAccounts } from "./accountDisplay";
import BankLogo from "./BankLogo";

const NO_FLOW: AccountSummary = { accountId: "", income: 0, expenses: 0, net: 0 };

/**
 * One card per linked account: its balance, then income, expenses and net for the summary window. The
 * figures come from the server, which already left out transfers and card payments, so these cards add
 * up to the totals above them. Names, masks and logos come from the accounts list.
 */
const AccountSummaryCards = ({ accounts, reloadKey }: { accounts: AccountSummary[]; reloadKey: number }) => {
  const [items, setItems] = useState<PlaidItem[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchPlaidItems()
      .then((result) => !cancelled && setItems(result))
      // The totals above still stand without this list, so a failure just leaves the section out.
      .catch(() => !cancelled && setItems([]));
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  if (!items || items.length === 0) return null;

  const byId = new Map(accounts.map((account) => [account.accountId, account]));

  return (
    <section className="mb-4" aria-label="By account">
      <h5 className="mb-3">
        By account <span className="summary-card__window">· last 30 days</span>
      </h5>
      <div className="row g-3">
        {items.flatMap((item) =>
          sortedAccounts(item).map((account) => (
            <div className="col-md-6 col-xl-4" key={account.id}>
              <AccountCard item={item} account={account} flow={byId.get(account.id) ?? NO_FLOW} />
            </div>
          )),
        )}
      </div>
    </section>
  );
};

const AccountCard = ({ item, account, flow }: { item: PlaidItem; account: LinkedAccount; flow: AccountSummary }) => {
  const isCredit = account.type === "credit";
  const name = account.name.replace(/^Plaid /, "");
  const kind = kindLabel(account);
  return (
    <div className="account-card">
      <div className="account-card__head">
        <BankLogo name={item.institution ?? "Bank"} logo={item.logo} color={item.primaryColor} />
        <div className="account-card__title">
          <span className="account-card__name">{name}</span>
          <span className="account-card__meta">
            {[item.institution, name.toLowerCase() === kind.toLowerCase() ? null : kind, `•••• ${account.mask ?? "····"}`]
              .filter(Boolean)
              .join(" · ")}
          </span>
        </div>
      </div>

      {account.currentBalance !== null && (
        <div className="account-card__balance">
          <span className="account-card__label">{balanceLabel(account)}</span>
          <span className="account-card__amount">{formatMoney(account.currentBalance)}</span>
          {account.balanceAsOf && <span className="account-card__meta">as of {ago(account.balanceAsOf)}</span>}
        </div>
      )}

      <dl className="account-card__flow">
        {!isCredit && (
          <div>
            <dt>Income</dt>
            <dd>{formatMoney(flow.income)}</dd>
          </div>
        )}
        <div>
          <dt>{isCredit ? "Spent" : "Expenses"}</dt>
          <dd>{formatMoney(flow.expenses)}</dd>
        </div>
        {!isCredit && (
          <div>
            <dt>Net</dt>
            <dd>{formatMoney(flow.net)}</dd>
          </div>
        )}
      </dl>
    </div>
  );
};

export default AccountSummaryCards;
