import { useState } from "react";
import AccountsSection from "../components/AccountsSection";

const Accounts = () => {
  const [reloadKey, setReloadKey] = useState(0);
  return <AccountsSection reloadKey={reloadKey} onChanged={() => setReloadKey((key) => key + 1)} />;
};

export default Accounts;
