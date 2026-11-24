using System.Text.RegularExpressions;
using backend.Models;

namespace backend.Services;

/// <summary>
/// Plaid's optional logo and brand colour for a bank. Both come from a third party and end up in an
/// image src and an inline style in the browser, so only values of the expected shape are kept.
/// Anything else becomes null and the UI falls back to the bank's initial.
/// </summary>
public static partial class InstitutionBranding
{
    public static string? CleanLogo(string? logo) =>
        !string.IsNullOrEmpty(logo) && logo.Length <= PlaidItem.MaxLogoLength && Base64().IsMatch(logo) ? logo : null;

    public static string? CleanColor(string? color) =>
        color is not null && HexColor().IsMatch(color) ? color : null;

    [GeneratedRegex("^[A-Za-z0-9+/]+={0,2}$")]
    private static partial Regex Base64();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
