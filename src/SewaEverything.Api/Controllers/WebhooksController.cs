using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SewaEverything.Api.Security;
using SewaEverything.Infrastructure.Payments;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("webhooks")]
[AllowAnonymous]
public sealed class WebhooksController(
    MidtransWebhookProcessor processor,
    ILogger<WebhooksController> logger) : ControllerBase
{
    [HttpPost("payment")]
    [Consumes("application/json")]
    [EnableRateLimiting(SewaRateLimits.Webhook)]
    [RequestSizeLimit(64 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Payment(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        var result = await processor.ProcessAsync(body, ct);

        switch (result.Outcome)
        {
            case WebhookOutcome.InvalidSignature:
                return Problem(
                    title: "Tanda tangan tidak sah",
                    detail: "Notifikasi ini tidak bisa dibuktikan berasal dari gateway.",
                    statusCode: StatusCodes.Status401Unauthorized);

            case WebhookOutcome.Malformed:
                return Problem(
                    title: "Payload tidak bisa dibaca",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status400BadRequest);

            case WebhookOutcome.Unknown:
                logger.LogWarning("Notifikasi untuk transaksi tak dikenal: {Detail}", result.Detail);
                return Ok(new { status = "ignored", detail = result.Detail });

            case WebhookOutcome.Duplicate:
                return Ok(new { status = "duplicate" });

            default:
                return Ok(new { status = "processed", detail = result.Detail });
        }
    }
}
