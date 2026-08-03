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
/// Login success body. The JWT itself is sent only in an HttpOnly cookie.
/// </summary>
public sealed record LoginResponse(
    DateTime ExpiresAt,
    string Email,
    string Name);

public sealed record MeResponse(
    Guid Id,
    string Email,
    string Name);
