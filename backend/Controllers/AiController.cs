using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using backend.DTOs;
using backend.Extensions;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

/// <summary>What the AI would receive. Every read is scoped to the user id in the validated token.</summary>
[ApiController]
[Route("api/ai")]
[Authorize(Policy = AuthSecurityExtensions.VerifiedEmailPolicy)]
public sealed class AiController(AiPayloadBuilder payloadBuilder) : ControllerBase
{
    /// <summary>
    /// The payload exactly as it would be sent, and the privacy report about it. Nothing is sent to any AI
    /// provider here. The report holds the caller's own descriptions and account endings, so the response
    /// is marked no-store and never cached by the browser or a proxy.
    /// </summary>
    [HttpGet("payload-preview")]
    [ProducesResponseType<AiPayloadPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AiPayloadPreviewResponse>> PayloadPreview(CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var result = await payloadBuilder.BuildAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(new AiPayloadPreviewResponse(
            result.Payload, PayloadPrivacyValidator.Serialize(result.Payload), result.Report));
    }
}
