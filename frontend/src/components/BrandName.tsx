import BrandLogo from "./BrandLogo";

export const BRAND_NAME = "VaultBudget AI";
/** UX-only cache (display name). Not used for API authorization. */
export const AUTH_USER_STORAGE_KEY = "vaultbudget-user";
/** Temporary Google stub flag until backend OAuth + cookie login exists. */
export const AUTH_GOOGLE_STUB_KEY = "vaultbudget-google-stub";

/** Clears client-side cached profile / Google stub flag. */
export const clearAuthSession = () => {
  localStorage.removeItem(AUTH_USER_STORAGE_KEY);
  localStorage.removeItem(AUTH_GOOGLE_STUB_KEY);
  // Remove legacy keys from earlier JWT-in-localStorage work.
  localStorage.removeItem("vaultbudget-auth");
  localStorage.removeItem("vaultbudget-token");
};

type BrandNameProps = {
  className?: string;
  showLogo?: boolean;
  logoSize?: "sm" | "md" | "lg" | "xl";
  layout?: "inline" | "stacked";
};

const BrandName = ({
  className,
  showLogo = true,
  logoSize = "sm",
  layout = "inline",
}: BrandNameProps) => (
  <span
    className={[
      "brand-name",
      showLogo ? `brand-name--${layout}` : "",
      className,
    ]
      .filter(Boolean)
      .join(" ")}
  >
    {showLogo && <BrandLogo size={logoSize} className="brand-name__logo" />}
    <span className="brand-name__text">
      <span className="brand-name__title">VaultBudget</span>{" "}
      <span className="brand-name__ai">AI</span>
    </span>
  </span>
);

export default BrandName;
