using Microsoft.Extensions.Options;
using Stripe;

namespace backend.Services;

/// <summary>The outbound Stripe calls. Faked in tests so no test reaches Stripe.</summary>
public interface IBillingGateway
{
    /// <summary>Creates a customer tagged with our user id only; Checkout collects the email itself.</summary>
    Task<string> CreateCustomerAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A hosted Checkout page for the subscription price. Card details never reach this API.</summary>
    Task<string> CreateCheckoutUrlAsync(string customerId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Stripe's hosted portal, where the user cancels or changes their card.</summary>
    Task<string> CreatePortalUrlAsync(string customerId, CancellationToken cancellationToken);
}

public sealed class StripeBillingGateway(IOptions<StripeOptions> options) : IBillingGateway
{
    private readonly StripeOptions _options = options.Value;
    private readonly StripeClient _client = new(options.Value.SecretKey);

    public async Task<string> CreateCustomerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var customer = await new CustomerService(_client).CreateAsync(
            new CustomerCreateOptions { Metadata = new Dictionary<string, string> { ["user_id"] = userId.ToString() } },
            cancellationToken: cancellationToken);
        return customer.Id;
    }

    public async Task<string> CreateCheckoutUrlAsync(string customerId, Guid userId, CancellationToken cancellationToken)
    {
        var session = await new Stripe.Checkout.SessionService(_client).CreateAsync(
            new Stripe.Checkout.SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                ClientReferenceId = userId.ToString(),
                LineItems = [new Stripe.Checkout.SessionLineItemOptions { Price = _options.PriceId, Quantity = 1 }],
                SuccessUrl = $"{_options.FrontendBaseUrl.TrimEnd('/')}/insights?checkout=success",
                CancelUrl = $"{_options.FrontendBaseUrl.TrimEnd('/')}/insights?checkout=cancelled",
            },
            cancellationToken: cancellationToken);
        return session.Url;
    }

    public async Task<string> CreatePortalUrlAsync(string customerId, CancellationToken cancellationToken)
    {
        var session = await new Stripe.BillingPortal.SessionService(_client).CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = $"{_options.FrontendBaseUrl.TrimEnd('/')}/insights",
            },
            cancellationToken: cancellationToken);
        return session.Url;
    }
}
