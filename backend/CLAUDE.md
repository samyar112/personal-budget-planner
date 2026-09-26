# Backend patterns

House style for `backend/` and `backend.Tests/`. Every snippet below is taken
from code already in this repository — match it rather than inventing a new
approach. Root rules are in `../CLAUDE.md`.

## DTOs

`sealed record`, positional, in `backend/DTOs/`. XML docs carry the *reasoning*,
not a restatement of the field names:

```csharp
/// <summary>
/// Login success body. Tokens are sent only in HttpOnly cookies.
/// </summary>
public sealed record LoginResponse(
    DateTime ExpiresAt,
    string Email,
    string Name);
```

Never put a token, hash, or secret in a response record.

## Entities and DbContext

Entities in `backend/Models/`, plain classes with `required` on non-nullable
fields. All relational configuration goes in `OnModelCreating` with the fluent
API — no data annotations:

```csharp
public DbSet<Transaction> Transactions => Set<Transaction>();

modelBuilder.Entity<RefreshToken>(entity =>
{
    entity.HasIndex(token => token.TokenHash).IsUnique();
    entity.HasIndex(token => token.UserId);
    entity.HasOne(token => token.User)
        .WithMany()
        .HasForeignKey(token => token.UserId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

Index every column you filter or join on. State delete behaviour explicitly.

## Migrations

```bash
dotnet ef migrations add <DescriptiveName> --project backend
dotnet ef database update --project backend
```

Name them for what they do — `AddRefreshTokens`, `AddLoginLockoutFields`.
Read the generated `Up`/`Down` before applying; EF guesses wrong on renames.
Never edit an applied migration — add a new one.

## Controllers

Attribute-routed under `api/`, returning `ActionResult<T>`, taking a
`CancellationToken`, and scoping every query to the authenticated user:

```csharp
[ApiController]
[Route("api/transactions")]
public sealed class TransactionsController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TransactionListResponse>> List(
        CancellationToken cancellationToken) { ... }
}
```

**Every query touching user data filters on `UserId` from the validated
token** — never from a route or body parameter. A missing filter leaks one
user's finances to another.

## Configuration

Options pattern, validated at startup so misconfiguration fails at boot:

```csharp
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Key) && o.Key.Length >= 32,
              "Jwt:Key must be at least 32 characters.")
    .ValidateOnStart();
```

Secrets come from `appsettings.Development.json` (git-ignored) locally and App
Service configuration in production. `appsettings.json` ships an empty
`DefaultConnection` on purpose — do not fill it in.

## Tests

`backend.Tests/`, xUnit, integration-first: boot the real API through
`WebApplicationFactory<Program>` rather than mocking. Each factory instance
gets its own in-memory database.

Open every test file with a numbered case manifest — this is a house
convention, keep it:

```csharp
/*
 * Test cases:
 * 1. Valid registration creates the user and hashes the password.
 * 2. Duplicate email returns conflict.
 */
public sealed class RegisterEndpointTests : IClassFixture<AuthWebApplicationFactory>
```

Conventions that matter:

- Unique data per test: `$"jane.{Guid.NewGuid():N}@example.com"`. A class
  fixture is shared, so fixed values collide
- Assert on `HttpStatusCode`, then on database state via a scoped
  `AppDbContext` — not just the response body
- For anything authorisation-shaped, include a test proving another user's
  rows are **not** visible. Watch it fail before you make it pass

Two factory styles, and the choice matters:

- **Shared** — `IClassFixture<AuthWebApplicationFactory>`, the default. One
  boot per class, fast. Used by the Register, Login and Refresh tests
- **Per-test** — build the factory inside the test when it needs different
  options or its own rate-limit and lockout state, which are process-wide and
  leak between tests otherwise:

```csharp
await using var factory = new RateLimitedAuthWebApplicationFactory();
```

To vary configuration, subclass the factory and override `ExtraConfig` rather
than editing the base. `RateLimitedAuthWebApplicationFactory` in
`AuthWebApplicationFactory.cs` drops `LoginPermitLimit` to 3; the base sets it
to 1000 so ordinary auth tests never trip the limiter.

```bash
dotnet test VaultBudget.slnx                              # all 18
dotnet test --filter "FullyQualifiedName~RegisterEndpoint"
```

## Security invariants

These hold across the whole API. Breaking one is a defect, not a design choice.

- Access and refresh tokens are issued **only** as HttpOnly cookies
- Refresh tokens are stored hashed, never in plaintext
- Failed-login state (`FailedLoginAttempts`, `LockoutEnd`) updates on every
  attempt, successful or not
- Auth responses do not reveal whether an account exists — same response and
  timing either way
