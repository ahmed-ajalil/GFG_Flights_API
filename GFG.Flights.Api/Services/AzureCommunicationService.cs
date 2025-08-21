using Azure.Communication.Messages;
using Azure.Communication.Messages.Models.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GFG.Flights.Api.Services
{
    /// <summary>
    /// Service wrapper over Azure Communication Services NotificationMessagesClient for WhatsApp.
    /// Follows a simplified, generic approach similar to the reference working project.
    /// </summary>
    public class AzureCommunicationService
    {
        private readonly NotificationMessagesClient _client;
        private readonly Guid _channelRegistrationGuid;
        private readonly ILogger<AzureCommunicationService> _logger;

        public AzureCommunicationService(IConfiguration configuration, ILogger<AzureCommunicationService> logger)
        {
            _logger = logger;
            var connectionString = configuration["AcsConnectionString"] ?? throw new ArgumentNullException("AcsConnectionString configuration is missing");
            var channelRegistrationId = configuration["AcsChannelRegistrationId"] ?? throw new ArgumentNullException("AcsChannelRegistrationId configuration is missing");
            _client = new NotificationMessagesClient(connectionString);
            _channelRegistrationGuid = Guid.Parse(channelRegistrationId);
            _logger.LogInformation("AzureCommunicationService initialized. Channel GUID: {Channel}", _channelRegistrationGuid);
        }

        /// <summary>
        /// Send a plain WhatsApp text message (no template) for diagnostics.
        /// </summary>
        public async Task SendTextMessageAsync(string phone, string message, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(phone)) throw new ArgumentException("Phone required", nameof(phone));
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message required", nameof(message));

            var op = Op();
            try
            {
                _logger.LogInformation("[{Op}] Sending plain text message to {Phone}", op, phone);
                var content = new TextNotificationContent(_channelRegistrationGuid, new List<string> { phone }, NormalizeNewlines(message));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await _client.SendAsync(content, ct);
                sw.Stop();
                _logger.LogInformation("[{Op}] Plain text sent in {Ms}ms Result={Result}", op, sw.ElapsedMilliseconds, JsonSerializer.Serialize(result.Value));
            }
            catch (Azure.RequestFailedException rfe)
            {
                _logger.LogError(rfe, "[{Op}] ACS RequestFailed Status={Status} Code={Code} Msg={Msg}", op, rfe.Status, rfe.ErrorCode, rfe.Message);
                throw;
            }
        }

        /// <summary>
        /// Generic template sender. Accepts already validated variable dictionary keyed by numeric placeholder indices (e.g. "1","2",...).
        /// </summary>
        public async Task SendTemplateMessageAsync(string phone, string templateName, string language, IReadOnlyDictionary<string, string> variables, IEnumerable<TemplateButton>? buttons = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(phone)) throw new ArgumentException("Phone required", nameof(phone));
            if (string.IsNullOrWhiteSpace(templateName)) throw new ArgumentException("Template name required", nameof(templateName));
            language ??= "en";
            variables ??= new Dictionary<string, string>();

            var op = Op();
            _logger.LogInformation("[{Op}] SendTemplate start Template={Template} Lang={Lang} Phone={Phone} VarCount={Cnt}", op, templateName, language, phone, variables.Count);

            try
            {
                var recipients = new List<string> { phone };
                var template = new MessageTemplate(templateName, language);

                // Add values using numeric keys required by ACS ({{1}}, {{2}}, etc.).
                foreach (var kvp in variables.OrderBy(k => ParseKey(k.Key)))
                {
                    var value = string.IsNullOrWhiteSpace(kvp.Value) ? " " : kvp.Value;
                    template.Values.Add(new MessageTemplateText(kvp.Key, value));
                    _logger.LogDebug("[{Op}] Added variable {Key}='{Value}'", op, kvp.Key, value);
                }

                // Build bindings (for body placeholders). For simple body-only templates ACS can infer, but explicit binding is safer.
                var bindings = new WhatsAppMessageTemplateBindings();
                foreach (var key in variables.Keys.OrderBy(ParseKey))
                {
                    // Body component referencing numeric key
                    bindings.Body.Add(new WhatsAppMessageTemplateBindingsComponent(key));
                }

                // Optional dynamic buttons: Only add bindings if provided. Do NOT add static buttons that are already part of approved template.
                if (buttons != null)
                {
                    foreach (var b in buttons)
                    {
                        // If button uses a variable reference (e.g. dynamic URL) we pass the variable key; if static we pass the literal value.
                        var reference = b.ReferenceKeyOrValue;
                        bindings.Buttons.Add(new WhatsAppMessageTemplateBindingsButton(b.Type, reference));
                        _logger.LogDebug("[{Op}] Added button Type={Type} Ref={Ref}", op, b.Type, reference);
                    }
                }

                template.Bindings = bindings;

                _logger.LogInformation("[{Op}] Template prepared Values={ValueCnt} BodyBindings={BodyCnt} ButtonBindings={BtnCnt}", op, template.Values.Count, bindings.Body.Count, bindings.Buttons.Count);

                var content = new TemplateNotificationContent(_channelRegistrationGuid, recipients, template);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = await _client.SendAsync(content, ct);
                sw.Stop();
                _logger.LogInformation("[{Op}] ACS send complete in {Ms}ms Raw={Raw}", op, sw.ElapsedMilliseconds, JsonSerializer.Serialize(response.Value));
            }
            catch (Azure.RequestFailedException rfe)
            {
                _logger.LogError(rfe, "[{Op}] ACS RequestFailed Status={Status} Code={Code} Msg={Msg}", op, rfe.Status, rfe.ErrorCode, rfe.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Op}] Unexpected error sending template", op);
                throw;
            }
        }

        private static string NormalizeNewlines(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return System.Text.RegularExpressions.Regex.Replace(text, "\n{3,}", "\n\n");
        }

        private static int ParseKey(string key)
        {
            return int.TryParse(key, out var n) ? n : int.MaxValue;
        }

        private static string Op() => Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// Button model for dynamic template buttons.
    /// </summary>
    public record TemplateButton(string Type, string ReferenceKeyOrValue);
}
