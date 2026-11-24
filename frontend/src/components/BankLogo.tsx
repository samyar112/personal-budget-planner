const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;
const BASE64 = /^[A-Za-z0-9+/]+={0,2}$/;

/** Black or white, whichever reads better on the colour. Plain luminance is enough for an initial. */
function textOn(hex: string): string {
  const [r, g, b] = [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16));
  return (r * 299 + g * 587 + b * 114) / 1000 > 150 ? "#1c2733" : "#ffffff";
}

/**
 * The bank's logo when Plaid has one, else its first letter on the brand colour. Plaid is a third
 * party, so the values are checked again here before they reach an img src or a style.
 */
const BankLogo = ({ name, logo, color }: { name: string; logo: string | null; color: string | null }) => {
  if (logo && BASE64.test(logo)) {
    return <img className="bank-logo" src={`data:image/png;base64,${logo}`} alt="" width={24} height={24} />;
  }
  const background = color && HEX_COLOR.test(color) ? color : "#64748b";
  return (
    <span className="bank-logo bank-logo--initial" style={{ background, color: textOn(background) }} aria-hidden="true">
      {name.trim().charAt(0).toUpperCase() || "?"}
    </span>
  );
};

export default BankLogo;
