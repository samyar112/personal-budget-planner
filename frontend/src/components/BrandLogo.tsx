export const BRAND_ICON_SRC = "/brand-icon.png";
export const BRAND_ICON_SM = "/brand-icon-48.png";
export const BRAND_ICON_MD = "/brand-icon-96.png";
export const BRAND_ICON_LG = "/brand-icon-160.png";

type BrandLogoProps = {
  size?: "sm" | "md" | "lg" | "xl";
  className?: string;
};

const sizeMap = {
  sm: { src: BRAND_ICON_SM, height: 36 },
  md: { src: BRAND_ICON_MD, height: 56 },
  lg: { src: BRAND_ICON_LG, height: 88 },
  xl: { src: BRAND_ICON_SRC, height: 120 },
} as const;

const BrandLogo = ({ size = "md", className }: BrandLogoProps) => {
  const { src, height } = sizeMap[size];

  return (
    <img
      src={src}
      alt=""
      aria-hidden="true"
      className={className ? `brand-logo ${className}` : "brand-logo"}
      style={{ height }}
    />
  );
};

export default BrandLogo;
