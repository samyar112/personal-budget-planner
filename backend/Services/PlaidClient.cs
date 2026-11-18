using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace backend.Services;

/// <summary>The Plaid calls this API makes. An interface so tests run against a fake, never the network.</summary>
public interface IPlaidClient
{
    /// <summary>Short-lived token the frontend hands to Plaid Link. <paramref name="userId"/> is our opaque id, never an email.</summary>
    Task<string> CreateLinkTokenAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Swaps the one-time public token from Link for a long-lived access token.</summary>
    Task<PlaidExchangeResult> ExchangePublicTokenAsync(string publicToken, CancellationToken cancellationToken);

    /// <summary>The item's accounts and institution.</summary>
    Task<PlaidItemDetails> GetItemDetailsAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed record PlaidExchangeResult(string AccessToken, string ItemId);

public sealed record PlaidItemDetails(string? InstitutionId, string? InstitutionName, IReadOnlyList<PlaidAccount> Accounts);

public sealed record PlaidAccount(string AccountId, string Name);

/// <summary>
/// Plaid refused a request. Carries Plaid's error code and request id for the log, never a
/// token or response body. <see cref="IsClientError"/> separates a bad public token from an outage.
/// </summary>
public sealed class PlaidException(HttpStatusCode statusCode, string? errorCode, string? requestId)
    : Exception($"Plaid returned {(int)statusCode} {errorCode ?? "(no error code)"}, request {requestId ?? "(none)"}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ErrorCode { get; } = errorCode;
    public bool IsClientError => (int)StatusCode is >= 400 and < 500;
}

/// <summary>Plain <see cref="HttpClient"/> over Plaid's JSON API; no SDK dependency.</summary>
public sealed class PlaidClient(HttpClient http, IOptions<PlaidOptions> options) : IPlaidClient
{
    private const string ClientName = "VaultBudget AI";
    private static readonly string[] CountryCodes = ["US"];

    private readonly PlaidOptions _options = options.Value;

    public async Task<string> CreateLinkTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var response = await PostAsync<LinkTokenCreateResponse>(
            "link/token/create",
            new
            {
                client_id = _options.ClientId,
                secret = _options.Secret,
                client_name = ClientName,
                user = new { client_user_id = userId.ToString() },
                products = new[] { "transactions" },
                country_codes = CountryCodes,
                language = "en",
            },
            cancellationToken);
        return response.LinkToken;
    }

    public async Task<PlaidExchangeResult> ExchangePublicTokenAsync(string publicToken, CancellationToken cancellationToken)
    {
        var response = await PostAsync<PublicTokenExchangeResponse>(
            "item/public_token/exchange",
            new { client_id = _options.ClientId, secret = _options.Secret, public_token = publicToken },
            cancellationToken);
        return new PlaidExchangeResult(response.AccessToken, response.ItemId);
    }

    public async Task<PlaidItemDetails> GetItemDetailsAsync(string accessToken, CancellationToken cancellationToken)
    {
        var accounts = await PostAsync<AccountsGetResponse>(
            "accounts/get",
            new { client_id = _options.ClientId, secret = _options.Secret, access_token = accessToken },
            cancellationToken);

        var institutionId = accounts.Item.InstitutionId;
        string? institutionName = null;
        if (!string.IsNullOrEmpty(institutionId))
        {
            var institution = await PostAsync<InstitutionGetResponse>(
                "institutions/get_by_id",
                new
                {
                    client_id = _options.ClientId,
                    secret = _options.Secret,
                    institution_id = institutionId,
                    country_codes = CountryCodes,
                },
                cancellationToken);
            institutionName = institution.Institution.Name;
        }

        return new PlaidItemDetails(
            institutionId,
            institutionName,
            accounts.Accounts.Select(account => new PlaidAccount(account.AccountId, account.Name)).ToList());
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(path, body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            PlaidError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<PlaidError>(cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
                // Not Plaid's error shape (a proxy page, say); the status code is enough.
            }

            throw new PlaidException(response.StatusCode, error?.ErrorCode, error?.RequestId);
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new PlaidException(response.StatusCode, "EMPTY_RESPONSE", null);
    }

    private sealed record PlaidError(
        [property: JsonPropertyName("error_code")] string? ErrorCode,
        [property: JsonPropertyName("request_id")] string? RequestId);

    private sealed record LinkTokenCreateResponse(
        [property: JsonPropertyName("link_token")] string LinkToken);

    private sealed record PublicTokenExchangeResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("item_id")] string ItemId);

    private sealed record AccountsGetResponse(
        [property: JsonPropertyName("accounts")] IReadOnlyList<AccountEntry> Accounts,
        [property: JsonPropertyName("item")] ItemEntry Item);

    private sealed record AccountEntry(
        [property: JsonPropertyName("account_id")] string AccountId,
        [property: JsonPropertyName("name")] string Name);

    private sealed record ItemEntry(
        [property: JsonPropertyName("institution_id")] string? InstitutionId);

    private sealed record InstitutionGetResponse(
        [property: JsonPropertyName("institution")] InstitutionEntry Institution);

    private sealed record InstitutionEntry(
        [property: JsonPropertyName("name")] string Name);
}
