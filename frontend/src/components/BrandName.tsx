export const BRAND_NAME = "VaultBudget AI";
export const AUTH_STORAGE_KEY = "vaultbudget-auth";

type BrandNameProps = {
  className?: string;
};

const BrandName = ({ className }: BrandNameProps) => (
  <span className={className ? `brand-name ${className}` : "brand-name"}>
    VaultBudget <span className="brand-name__ai">AI</span>
  </span>
);

export default BrandName;
