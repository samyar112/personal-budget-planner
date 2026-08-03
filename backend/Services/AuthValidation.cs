using System.Text.RegularExpressions;

namespace backend.Services;

/// <summary>
/// Shared registration validation rules (mirror of Landing.tsx client checks).
/// </summary>
public static partial class AuthValidation
{
    private static readonly Regex EmailPattern = EmailRegex();
    private static readonly Regex NamePattern = NameRegex();

    public static Dictionary<string, string> ValidateRegister(
        string firstName,
        string lastName,
        string email,
        string password)
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var firstNameError = GetNameError(firstName, "First name");
        if (firstNameError is not null)
        {
            errors["firstName"] = firstNameError;
        }

        var lastNameError = GetNameError(lastName, "Last name");
        if (lastNameError is not null)
        {
            errors["lastName"] = lastNameError;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = "Email is required.";
        }
        else if (!EmailPattern.IsMatch(email.Trim()))
        {
            errors["email"] = "Please enter a valid email address.";
        }

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = "Password is required.";
        }
        else if (!IsPasswordValid(password))
        {
            errors["password"] = "Password does not meet all requirements.";
        }

        return errors;
    }

    public static bool IsPasswordValid(string password) =>
        password.Length >= 8
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        && password.Any(c => !char.IsLetterOrDigit(c));

    private static string? GetNameError(string value, string label)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return $"{label} is required.";
        }

        if (trimmed.Length > 50)
        {
            return $"{label} must be 50 characters or less.";
        }

        if (!NamePattern.IsMatch(trimmed))
        {
            return $"Enter a valid {label.ToLowerInvariant()}.";
        }

        return null;
    }

    [GeneratedRegex(@"^\S+@\S+\.\S+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    // Letters (incl. accents); spaces, hyphens, and apostrophes between name parts.
    [GeneratedRegex(@"^[\p{L}]+(?:[ '\-][\p{L}]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
