import BrandLogo from "./BrandLogo";

export const BRAND_NAME = "VaultBudget AI";
export const AUTH_STORAGE_KEY = "vaultbudget-auth";
export const AUTH_USER_STORAGE_KEY = "vaultbudget-user";

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
