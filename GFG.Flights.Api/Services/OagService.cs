using System.Text.Json;
using GFG.Flights.Api.Models;

namespace GFG.Flights.Api.Services;

public interface IOagService
{
    Task<OagFlightInstance?> GetFlightStatusAsync(string flightNumber, DateOnly date, CancellationToken ct = default);
}

public sealed class OagService : IOagService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OagService> _logger;
    private readonly string _subscriptionKey;

    public OagService(HttpClient httpClient, IConfiguration configuration, ILogger<OagService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _subscriptionKey = configuration["OagSubscriptionKey"] ?? 
            throw new InvalidOperationException("OagSubscriptionKey is required in configuration");
        
        _httpClient.BaseAddress = new Uri("https://api.oag.com/");
        _httpClient.DefaultRequestHeaders.Add("Subscription-Key", _subscriptionKey);
    }

    public async Task<OagFlightInstance?> GetFlightStatusAsync(string flightNumber, DateOnly date, CancellationToken ct = default)
    {
        try
        {
            // Normalize flight number - remove leading zeros for OAG API
            var normalizedFlightNumber = int.Parse(flightNumber.TrimStart('0')).ToString();
            var dateString = date.ToString("yyyy-MM-dd");

            var url = $"flight-instances/?DepartureDateTime={dateString}&CarrierCode=GF&FlightNumber={normalizedFlightNumber}&Content=status,map&CodeType=IATA&version=v2";
            
            _logger.LogDebug("Calling OAG API: {Url}", url);

            var response = await _httpClient.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OAG API returned {StatusCode} for flight {FlightNumber} on {Date}", 
                    response.StatusCode, flightNumber, date);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var oagResponse = JsonSerializer.Deserialize<OagResponse>(content, options);
            
            var flightInstance = oagResponse?.Data?.FirstOrDefault();
            
            if (flightInstance != null)
            {
                _logger.LogDebug("Successfully retrieved OAG data for flight {FlightNumber} on {Date}", 
                    flightNumber, date);
            }
            else
            {
                _logger.LogDebug("No OAG data found for flight {FlightNumber} on {Date}", 
                    flightNumber, date);
            }

            return flightInstance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OAG API for flight {FlightNumber} on {Date}", 
                flightNumber, date);
            return null;
        }
    }
}