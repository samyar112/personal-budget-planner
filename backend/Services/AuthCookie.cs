namespace backend.Services;

/// <summary>
/// Shared access-token cookie name and options (HttpOnly; Secure outside Development).
/// </summary>
public static class AuthCookie
{
    public const string AccessTokenName = "vb_access_token";

    public static CookieOptions Create(bool isDevelopment, DateTimeOffset? expires = null) =>
        new()
        {
            HttpOnly = true,
            Secure = !isDevelopment,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
            IsEssential = true,
        };

    public static void SetAccessToken(HttpResponse response, string token, DateTime expiresAt, bool isDevelopment)
    {
        response.Cookies.Append(
            AccessTokenName,
            token,
            Create(isDevelopment, new DateTimeOffset(expiresAt)));
    }

    public static void ClearAccessToken(HttpResponse response, bool isDevelopment)
    {
        response.Cookies.Delete(AccessTokenName, Create(isDevelopment));
    }
}
