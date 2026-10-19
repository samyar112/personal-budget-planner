namespace backend.DTOs;

public sealed record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password);

public sealed record RegisterResponse(
    Guid Id,
    string Email,
    string Name);

public sealed record LoginRequest(
    string Email,
    string Password);

/// <summary>
/// Login success body. Tokens are sent only in HttpOnly cookies.
/// </summary>
public sealed record LoginResponse(
    DateTime ExpiresAt,
    string Email,
    string Name);

/// <summary>
/// Refresh success body. New tokens are sent only in HttpOnly cookies.
/// </summary>
public sealed record RefreshResponse(DateTime ExpiresAt);

public sealed record MeResponse(
    Guid Id,
    string Email,
    string Name,
    bool EmailVerified);

/// <summary>The raw token from the emailed link. Never logged or stored.</summary>
public sealed record VerifyEmailRequest(string Token);

public sealed record ResendVerificationRequest(string Email);

public sealed record ForgotPasswordRequest(string Email);

/// <summary>The raw token from the emailed link plus the new password. Never logged.</summary>
public sealed record ResetPasswordRequest(string Token, string NewPassword);


/// <summary>
/// Step-up challenge returned instead of cookies when a code has been emailed. The code
/// itself never appears here.
/// </summary>
public sealed record LoginChallengeResponse(
    Guid ChallengeId,
    DateTime ResendAvailableAt);

/// <summary>The challenge id from login plus the 6-digit code from the email. Never logged.</summary>
public sealed record VerifyLoginCodeRequest(Guid ChallengeId, string Code);

public sealed record ResendLoginCodeRequest(Guid ChallengeId);
