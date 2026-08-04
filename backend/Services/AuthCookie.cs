namespace backend.Services;

/// <summary>
/// Shared auth cookie names and options (HttpOnly; Secure outside Development).
/// </summary>
public static class AuthCookie
{
    public const string AccessTokenName = "vb_access_token";
    public const string RefreshTokenName = "vb_refresh_token";

    /// <summary>Scoped so logout and refresh both receive the cookie.</summary>
    public const string RefreshCookiePath = "/api/auth";

    public static CookieOptions CreateAccess(bool isDevelopment, DateTimeOffset? expires = null) =>
        new()
        {
            HttpOnly = true,
            Secure = !isDevelopment,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
            IsEssential = true,
        };

    public static CookieOptions CreateRefresh(bool isDevelopment, DateTimeOffset? expires = null) =>
        new()
        {
            HttpOnly = true,
            Secure = !isDevelopment,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            Expires = expires,
            IsEssential = true,
        };

    public static void SetAccessToken(HttpResponse response, string token, DateTime expiresAt, bool isDevelopment)
    {
        response.Cookies.Append(
            AccessTokenName,
            token,
            CreateAccess(isDevelopment, new DateTimeOffset(expiresAt)));
    }

    public static void SetRefreshToken(HttpResponse response, string token, DateTime expiresAt, bool isDevelopment)
    {
        response.Cookies.Append(
            RefreshTokenName,
            token,
            CreateRefresh(isDevelopment, new DateTimeOffset(expiresAt)));
    }

    public static void ClearAccessToken(HttpResponse response, bool isDevelopment)
    {
        response.Cookies.Delete(AccessTokenName, CreateAccess(isDevelopment));
    }

    public static void ClearRefreshToken(HttpResponse response, bool isDevelopment)
    {
        response.Cookies.Delete(RefreshTokenName, CreateRefresh(isDevelopment));
    }

    public static void ClearAll(HttpResponse response, bool isDevelopment)
    {
        ClearAccessToken(response, isDevelopment);
        ClearRefreshToken(response, isDevelopment);
    }
}
