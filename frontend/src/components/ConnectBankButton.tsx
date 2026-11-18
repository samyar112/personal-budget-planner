import { useState } from "react";
import { ApiError } from "../api/auth";
import { createLinkToken, exchangePublicToken, type PlaidExchangeResult } from "../api/plaid";

/** Plaid's own loader. Loaded from Plaid's CDN, as Plaid requires, rather than bundled from npm. */
const PLAID_SCRIPT_URL = "https://cdn.plaid.com/link/v2/stable/link-initialize.js";

type PlaidHandler = { open: () => void; destroy: () => void };

type PlaidGlobal = {
  create: (config: {
    token: string;
    onSuccess: (publicToken: string) => void;
    onExit: (error: unknown) => void;
  }) => PlaidHandler;
};

declare global {
  interface Window {
    Plaid?: PlaidGlobal;
  }
}

let scriptPromise: Promise<PlaidGlobal> | null = null;

/** Injects the Plaid script once per page load and resolves with window.Plaid. */
function loadPlaid(): Promise<PlaidGlobal> {
  if (window.Plaid) return Promise.resolve(window.Plaid);
  scriptPromise ??= new Promise<PlaidGlobal>((resolve, reject) => {
    const script = document.createElement("script");
    script.src = PLAID_SCRIPT_URL;
    script.async = true;
    script.onload = () => (window.Plaid ? resolve(window.Plaid) : reject(new Error("Plaid did not load.")));
    script.onerror = () => {
      scriptPromise = null;
      script.remove();
      reject(new Error("Plaid did not load."));
    };
    document.head.appendChild(script);
  });
  return scriptPromise;
}

type Status =
  | { kind: "idle" }
  | { kind: "busy" }
  | { kind: "linked"; result: PlaidExchangeResult }
  | { kind: "error"; message: string };

/** Opens Plaid Link, then stores the linked bank through the API. */
const ConnectBankButton = ({ onLinked }: { onLinked?: (result: PlaidExchangeResult) => void }) => {
  const [status, setStatus] = useState<Status>({ kind: "idle" });

  const handleClick = async () => {
    setStatus({ kind: "busy" });
    try {
      const [plaid, token] = await Promise.all([loadPlaid(), createLinkToken()]);
      const handler = plaid.create({
        token,
        onSuccess: (publicToken) => {
          handler.destroy();
          exchangePublicToken(publicToken)
            .then((result) => {
              setStatus({ kind: "linked", result });
              onLinked?.(result);
            })
            .catch((err: unknown) =>
              setStatus({
                kind: "error",
                message: err instanceof ApiError ? err.message : "Could not save the bank connection. Try again.",
              }),
            );
        },
        onExit: (error) => {
          handler.destroy();
          setStatus(
            error
              ? { kind: "error", message: "The bank connection was interrupted. Try again." }
              : { kind: "idle" },
          );
        },
      });
      handler.open();
    } catch (err) {
      setStatus({
        kind: "error",
        message: err instanceof ApiError ? err.message : "Could not start the bank connection. Try again.",
      });
    }
  };

  // A fragment, so the button sits in the parent's flex row like any sibling button. The status line
  // takes the full width and moves after every button, so its length never shifts the buttons.
  return (
    <>
      <button
        type="button"
        className="btn btn-success text-nav"
        onClick={handleClick}
        disabled={status.kind === "busy"}
      >
        {status.kind === "busy" ? "Connecting…" : "Connect a Bank"}
      </button>

      {status.kind === "linked" && (
        <div className="small w-100 order-last text-center" role="status">
          Linked {status.result.institution ?? "your bank"}:{" "}
          {status.result.accounts.map((account) => account.name).join(", ")}
        </div>
      )}

      {status.kind === "error" && (
        <div className="small text-danger w-100 order-last text-center" role="alert">
          {status.message}
        </div>
      )}
    </>
  );
};

export default ConnectBankButton;
