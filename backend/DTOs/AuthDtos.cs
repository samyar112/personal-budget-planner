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
    string Name);
