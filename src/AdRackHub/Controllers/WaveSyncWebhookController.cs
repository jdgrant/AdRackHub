using AdRackHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdRackHub.Controllers;

[ApiController]
[Route("api/wave-sync")]
[AllowAnonymous]
public class WaveSyncWebhookController : ControllerBase
{
    private readonly WaveSyncService _syncService;
    private readonly WaveSyncOptions _options;

    public WaveSyncWebhookController(WaveSyncService syncService, IOptions<WaveSyncOptions> options)
    {
        _syncService = syncService;
        _options = options.Value;
    }

    [HttpGet("")]
    public IActionResult Health()
    {
        if (!IsAuthorized())
            return Unauthorized(new { success = false, message = "Invalid webhook secret." });

        return Ok(new
        {
            success = true,
            message = "AdRackHub Wave sync webhook is ready.",
            inboundConfigured = _syncService.IsInboundConfigured,
            outboundConfigured = _syncService.IsOutboundConfigured
        });
    }

    /// <summary>
    /// Zapier posts here when a new customer is created in Wave.
    /// </summary>
    [HttpPost("customers")]
    public async Task<ActionResult<WaveSyncWebhookResponse>> ImportCustomer(
        [FromBody] InboundWaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
            return Unauthorized(WaveSyncWebhookResponse.Fail("Invalid webhook secret."));

        if (!ModelState.IsValid)
            return BadRequest(WaveSyncWebhookResponse.Fail("Invalid customer payload."));

        var result = await _syncService.ImportCustomerFromWaveAsync(request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Optional callback for Zapier/Make to report Wave invoice number and status
    /// (Submitted, Received, or Canceled) after creating an invoice.
    /// </summary>
    [HttpPost("invoices/callback")]
    public async Task<ActionResult<WaveSyncWebhookResponse>> InvoiceCallback(
        [FromBody] InboundWaveInvoiceCallbackRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
            return Unauthorized(WaveSyncWebhookResponse.Fail("Invalid webhook secret."));

        if (!ModelState.IsValid)
            return BadRequest(WaveSyncWebhookResponse.Fail("Invalid invoice callback payload."));

        var result = await _syncService.RecordInvoiceCallbackAsync(request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Optional callback for Zapier to report the Wave recurring invoice ID after creating it.
    /// </summary>
    [HttpPost("billing/callback")]
    public async Task<ActionResult<WaveSyncWebhookResponse>> BillingCallback(
        [FromBody] InboundWaveContractCallbackRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
            return Unauthorized(WaveSyncWebhookResponse.Fail("Invalid webhook secret."));

        if (!ModelState.IsValid)
            return BadRequest(WaveSyncWebhookResponse.Fail("Invalid billing callback payload."));

        var result = await _syncService.RecordContractCallbackAsync(request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    private bool IsAuthorized()
    {
        var secret = _options.InboundWebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        var provided = Request.Headers["X-AdRackHub-Webhook-Secret"].FirstOrDefault()
            ?? Request.Query["secret"].FirstOrDefault();

        return string.Equals(secret, provided, StringComparison.Ordinal);
    }
}
