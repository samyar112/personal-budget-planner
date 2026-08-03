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
