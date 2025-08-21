using Microsoft.AspNetCore.Mvc;
using GFG.Flights.Api.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;

namespace GFG.Flights.Api.Controllers;

[ApiController]
[Route("api/whatsapp")] // simplified route
[Produces("application/json")]
public class WhatsAppController : ControllerBase
{
    private readonly AzureCommunicationService _svc;
    private readonly ILogger<WhatsAppController> _logger;

    public WhatsAppController(AzureCommunicationService svc, ILogger<WhatsAppController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    /// <summary>
    /// Send an approved WhatsApp template message.
    /// Supply variables as an ordered array OR a dictionary keyed by numeric placeholder ("1","2",...).
    /// </summary>
    /// <remarks>
    /// Examples:
    /// POST /api/whatsapp/template
    /// {
    ///   "phone": "+97300000000",
    ///   "template": "jfk_after_boarding",
    ///   "language": "en",
    ///   "variables": ["John Doe","New York (JFK)","1","7"]
    /// }
    /// OR
    /// {
    ///   "phone": "+97300000000",
    ///   "template": "jfk_after_checkin",
    ///   "language": "en",
    ///   "variablesMap": {"1":"John Doe","2":"Bahrain (BAH)","3":"New York (JFK)","4":"04:00 PM","5":"11A"}
    /// }
    /// </remarks>
    [HttpPost("template")]
    [SwaggerOperation(Summary = "Send WhatsApp template", Description = "Send an approved WhatsApp template with variables.")]
    [SwaggerResponse(200, "Template sent")] 
    [SwaggerResponse(400, "Validation error")] 
    [SwaggerResponse(500, "Internal error")]
    public async Task<IActionResult> SendTemplate([FromBody] SendTemplateRequest request, CancellationToken ct)
    {
        var reqId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("[{Req}] Incoming template send request Template={Template}", reqId, request.Template);

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        try
        {
            // Build variable map: if variables array provided, map index-> (index+1) numeric key.
            IDictionary<string,string> vars = new Dictionary<string,string>();
            if (request.Variables != null && request.Variables.Length > 0)
            {
                for (int i=0;i<request.Variables.Length;i++)
                {
                    vars[(i+1).ToString()] = request.Variables[i] ?? " ";
                }
            }
            if (request.VariablesMap != null)
            {
                foreach (var kv in request.VariablesMap)
                {
                    vars[kv.Key] = kv.Value ?? " ";
                }
            }
            if (vars.Count == 0)
            {
                return BadRequest(new { error = "At least one variable must be supplied via variables or variablesMap", reqId });
            }

            // Optional: basic max variable count guard (ACS limit typical 30)
            if (vars.Count > 30) return BadRequest(new { error = "Too many variables", reqId });

            // Validate numeric keys only
            if (vars.Keys.Any(k => !int.TryParse(k, out _)))
            {
                return BadRequest(new { error = "VariablesMap keys must be numeric strings (e.g. '1','2')", reqId });
            }

            var maskedPhone = MaskPhone(request.Phone);
            _logger.LogInformation("[{Req}] Sending template {Template} to {Phone} VarKeys=[{Keys}]", reqId, request.Template, maskedPhone, string.Join(',', vars.Keys.OrderBy(k=>int.Parse(k))));

            await _svc.SendTemplateMessageAsync(request.Phone!, request.Template!, request.Language ?? "en", (IReadOnlyDictionary<string,string>)vars, ct: ct);
            return Ok(new { message = "Template sent", requestId = reqId });
        }
        catch (Azure.RequestFailedException rfe)
        {
            _logger.LogError(rfe, "[{Req}] ACS failure Status={Status} Code={Code} Msg={Msg}", reqId, rfe.Status, rfe.ErrorCode, rfe.Message);
            return StatusCode(502, new { error = "ACS error", status = rfe.Status, code = rfe.ErrorCode, requestId = reqId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Req}] Unexpected error", reqId);
            return StatusCode(500, new { error = ex.Message, requestId = reqId });
        }
    }

    /// <summary>
    /// Simple health check / diagnostics endpoint.
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "ok", time = DateTime.UtcNow });

    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 6) return "****";
        return phone[..4] + new string('*', Math.Max(0, phone.Length - 6)) + phone[^2..];
    }
}

public class SendTemplateRequest
{
    [Required]
    public string? Phone { get; set; }
    [Required]
    public string? Template { get; set; }
    public string? Language { get; set; } = "en";
    public string[]? Variables { get; set; } // ordered list
    public Dictionary<string,string>? VariablesMap { get; set; } // keyed
}