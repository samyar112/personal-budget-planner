namespace backend.DTOs;

/// <summary>A Stripe-hosted page to send the browser to. Single-use and short-lived, so never cached.</summary>
public sealed record BillingRedirectResponse(string Url);
