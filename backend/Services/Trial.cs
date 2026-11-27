using backend.Models;

namespace backend.Services;

/// <summary>
/// The free trial, derived from <see cref="User.DateEntered"/>, and who may use AI tips. Admins have no trial.
/// </summary>
public static class Trial
{
    public const int LengthDays = 14;

    /// <summary>Null for admins, who are never gated.</summary>
    public static DateTime? EndsAt(User user) =>
        user.Role == UserRole.Admin ? null : user.DateEntered.AddDays(LengthDays);

    /// <summary>Whole days left, rounded up so a new user sees 14. Never below 0. Null for admins.</summary>
    public static int? DaysRemaining(User user, DateTime now) =>
        EndsAt(user) is { } end ? Math.Max(0, (int)Math.Ceiling((end - now).TotalDays)) : null;

    public static bool IsExpired(User user, DateTime now) => EndsAt(user) is { } end && now >= end;

    /// <summary>Stripe's paying states. <c>past_due</c> loses access at once; Stripe retries the card meanwhile.</summary>
    public static bool IsSubscribed(User user) => user.SubscriptionStatus is "active" or "trialing";

    public static bool HasAiAccess(User user, DateTime now) => IsSubscribed(user) || !IsExpired(user, now);
}
