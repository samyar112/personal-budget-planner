using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace backend.Services;

/// <summary>The one outbound AI call. An interface so tests run against a fake, never the network.</summary>
public interface IAdvisorModel
{
    /// <summary>False when no key or model is set. Callers then use template text without calling out.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends one system and one user message and returns the reply text and the model that wrote it.
    /// Throws <see cref="AdvisorModelException"/> when the provider refuses or returns nothing usable.
    /// </summary>
    Task<AdvisorModelReply> CompleteAsync(string systemPrompt, string userContent, CancellationToken cancellationToken);
}

public sealed record AdvisorModelReply(string Content, string Model);

/// <summary>The provider failed. Carries the status code only, never the request or response body.</summary>
public sealed class AdvisorModelException(string message) : Exception(message);

/// <summary>
/// Plain <see cref="HttpClient"/> over OpenRouter's OpenAI-compatible chat API; no SDK dependency.
/// Every request asks for providers that do not collect prompts, so a provider that trains on data is never
/// used even if the account setting allows it. If no such provider serves the model, the call fails and the
/// caller falls back to template text.
/// </summary>
public sealed class OpenRouterAdvisorModel(HttpClient http, IOptions<AiAdvisorOptions> options) : IAdvisorModel
{
    private readonly AiAdvisorOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<AdvisorModelReply> CompleteAsync(
        string systemPrompt,
        string userContent,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userContent },
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "tips",
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(AiTips.ReplySchema),
                },
            },
            ["temperature"] = 0.2,
            // Reasoning models spend output tokens thinking first; at 1200 with default effort ERNIE returned
            // nothing. Low effort, with the reasoning left out of the reply, fits comfortably.
            ["max_tokens"] = 2000,
            ["reasoning"] = new JsonObject { ["effort"] = "low", ["exclude"] = true },
            ["provider"] = new JsonObject { ["data_collection"] = "deny" },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Add("X-Title", "VaultBudget AI");

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AdvisorModelException($"AI provider returned {(int)response.StatusCode}.");
        }

        ChatResponse? chat;
        try
        {
            chat = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
        }
        catch (JsonException)
        {
            throw new AdvisorModelException("AI provider returned a body that is not JSON.");
        }

        var content = chat?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AdvisorModelException("AI provider returned no content.");
        }

        return new AdvisorModelReply(content, chat!.Model ?? _options.Model);
    }

    private sealed record ChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] List<ChatChoice>? Choices);

    private sealed record ChatChoice([property: JsonPropertyName("message")] ChatMessage? Message);

    private sealed record ChatMessage([property: JsonPropertyName("content")] string? Content);
}
