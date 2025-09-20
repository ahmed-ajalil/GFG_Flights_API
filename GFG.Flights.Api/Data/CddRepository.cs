using Dapper;
using GFG.Flights.Api.Models;
using GFG.Flights.Api.Services;
using Newtonsoft.Json;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Net.Http;
using System.Text;

namespace GFG.Flights.Api.Data
{
    public class CddRepository : ICddRepository
    {
        private readonly Func<string, IDbConnection> _db;
        private readonly IOagService _oagService;
        private readonly ILogger<CddRepository> _logger;
        // Add the required field for IHttpClientFactory
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public CddRepository(Func<string, IDbConnection> dbFactory, IOagService oagService, ILogger<CddRepository> logger)
        {
            _db = dbFactory;
            _oagService = oagService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<FlightDto>> GetTodaysFlightsAsync(CancellationToken ct)
        {
            using var conn = (OracleConnection)_db("CDD");
            const string sql = @"
SELECT DISTINCT
  TRIM(t.FLIGHT_NUMBER) AS FlightNumber,
  TRIM(t.SERVICE_START_CITY) AS Departure,
  TRIM(t.SERVICE_END_CITY)   AS Arrival,
  TO_DATE(TO_CHAR(t.SERVICE_START_DATE,'YYYY-MM-DD') || ' ' || t.SERVICE_START_TIME,'YYYY-MM-DD HH24:MI:SS') AS StdUtc
FROM vw_pax_details t
WHERE TRUNC(t.SERVICE_START_DATE) = TRUNC(SYSDATE)
  AND TRIM(t.AIRLINE_CODE) = 'GF'
ORDER BY StdUtc";
            var rows = await conn.QueryAsync<FlightDto>(new CommandDefinition(sql, cancellationToken: ct));
            return rows.ToList();
        }
        public async Task<IReadOnlyList<PassengerDto>> GetBookedPassengersAsync(string flightNumber, DateOnly date, CancellationToken ct)
        {
            using var conn = (OracleConnection)_db("CDD");

            const string sql = @"
SELECT
  TRIM(t.PNR_LOCATOR)     AS Pnr,
  TRIM(t.PASSENGER_NAME)  AS FullName,
  TRIM(t.CLASS_OF_SERVICE) AS ClassOfService,
  TRIM(t.TICKET_NUMBER)    AS TicketNumber,
  t.PHONE_NUMBER  AS PhoneNumber,
  TRIM(t.FLIGHT_NUMBER)    AS FlightNumber,
  TRUNC(t.SERVICE_START_DATE)     AS ServiceStartDate
FROM vw_pax_details t
WHERE TRIM(t.FLIGHT_NUMBER) = :flightNumber
  AND TRUNC(t.SERVICE_START_DATE) = :flightDate
  AND (t.PASSENGER_STATUS IS NULL OR UPPER(t.PASSENGER_STATUS) IN ('BOOKED','CONFIRMED'))
  AND t.PHONE_NUMBER IS NOT NULL
  AND t.coupon_status not in ('CKIN')
ORDER BY t.PASSENGER_NAME";

            var args = new
            {
                flightNumber = flightNumber.Trim(),
                flightDate = date.ToDateTime(TimeOnly.MinValue)
            };

            var raw = await conn.QueryAsync<CddBookedRow>(new CommandDefinition(sql, args, cancellationToken: ct));
            var passengers = raw.Select(r =>
            {
                var (given, surname) = SplitNameSurnameFirst(r.FullName);
                var phoneNumber = r.PhoneNumber?.Replace("-M", "").Replace("-H-1.1", "");

                return new PassengerDto(
                    r.Pnr,
                    given,
                    surname,
                    phoneNumber,
                    r.FlightNumber,
                    r.ServiceStartDate.HasValue ? DateOnly.FromDateTime(r.ServiceStartDate.Value) : (DateOnly?)null
                );
                
            }).ToList();

            // Check if test mode is enabled
            var useTestData = _configuration.GetValue<bool>("WhatsAppApi:UseTestData", false);
            
            if (useTestData)
            {
                _logger.LogWarning("TEST MODE ENABLED - Using dummy passenger data for check-in reminders");
                
                // Create dummy test data
                var testPassengers = CreateTestPassengers(flightNumber, date);
                
                if (testPassengers.Any())
                {
                    _logger.LogInformation("TEST MODE: Initiating check-in reminders for flight {FlightNumber} with {Count} test passengers",
                        flightNumber, testPassengers.Count);
                    
                    // Fire and forget - don't wait for WhatsApp messages to complete
                    _ = Task.Run(async () => await SendCheckInRemindersAsync(testPassengers, ct));
                }
            }
            else if (passengers.Any())
            {
                _logger.LogInformation("Initiating online check-in reminders for flight {FlightNumber} with {Count} passengers",
                    flightNumber, passengers.Count);

                // Fire and forget - don't wait for WhatsApp messages to complete
                _ = Task.Run(async () => await SendCheckInRemindersAsync(passengers, ct));
            }

            return passengers;
        }

        private IReadOnlyList<PassengerDto> CreateTestPassengers(string flightNumber, DateOnly date)
        {
            // Get test phone numbers from configuration
            var testPhoneNumbers = _configuration.GetSection("WhatsAppApi:TestPhoneNumbers")
                .Get<List<TestPhoneConfig>>() ?? new List<TestPhoneConfig>();

            if (!testPhoneNumbers.Any())
            {
                _logger.LogWarning("No test phone numbers configured. Add WhatsAppApi:TestPhoneNumbers to appsettings");
                return new List<PassengerDto>();
            }

            var testPassengers = testPhoneNumbers.Select((config, index) => new PassengerDto(
                Pnr: $"TEST{(index + 1):D3}",
                GivenName: config.Name?.Split(' ').FirstOrDefault() ?? $"Test{index + 1}",
                Surname: config.Name?.Split(' ').Skip(1).FirstOrDefault() ?? "",
                SeatOrPhone: config.PhoneNumber,
                FlightNumber: flightNumber,
                FlightDate: date
            )).ToList();

            _logger.LogInformation("Created {Count} test passengers: {Passengers}", 
                testPassengers.Count,
                string.Join(", ", testPassengers.Select(p => $"{p.GivenName} {p.Surname} ({p.SeatOrPhone})")));

            return testPassengers;
        }

        private class TestPhoneConfig
        {
            public string Name { get; set; } = string.Empty;
            public string PhoneNumber { get; set; } = string.Empty;
        }

        private async Task SendCheckInRemindersAsync(IReadOnlyList<PassengerDto> passengers, CancellationToken ct)
        {
            try
            {
                // Prepare the batch payload
                var batchPayload = passengers
                    .Where(p => !string.IsNullOrWhiteSpace(p.SeatOrPhone)) // Only include passengers with phone numbers
                    .Select(p => new
                    {
                        pnr = p.Pnr,
                        givenName = p.GivenName,
                        surname = p.Surname,
                        seatOrPhone = p.SeatOrPhone,
                        flightNumber = p.FlightNumber?.Replace("GF", "").TrimStart('0'), // Remove GF prefix and leading zeros
                        flightDate = p.FlightDate?.ToString("yyyy-MM-dd")
                    })
                    .ToList();

                if (!batchPayload.Any())
                {
                    _logger.LogInformation("No passengers with valid phone numbers to send reminders");
                    return;
                }

                // Get WhatsApp API configuration
                var apiUrl = _configuration["WhatsAppApi:BaseUrl"];

                if (string.IsNullOrEmpty(apiUrl))
                {
                    _logger.LogWarning("WhatsApp API URL not configured, skipping check-in reminders");
                    return;
                }

                // Build request URL
                var requestUrl = $"{apiUrl}/api/checkin/online/batch";

                // Send the request
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for batch operations

                var json = JsonConvert.SerializeObject(batchPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("Sending check-in reminders for {Count} passengers", batchPayload.Count);

                var response = await httpClient.PostAsync(requestUrl, content, ct);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(ct);
                    dynamic result = JsonConvert.DeserializeObject(responseBody);

                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError(
                        "WhatsApp API returned error {StatusCode}: {Content}",
                        response.StatusCode,
                        errorContent);
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Check-in reminder batch was cancelled (timeout or cancellation token)");
            }
            catch (Exception ex)
            {
                // Log but don't throw - this is a background task
                _logger.LogError(ex, "Failed to send check-in reminders batch");
            }
        }
        // Enriched flights between date range - now with OAG data integration
        public async Task<IReadOnlyList<FlightStatusDto>> GetFlightsAsync(DateOnly from, DateOnly to, CancellationToken ct)
        {
            using var conn = (OracleConnection)_db("CDD");
            const string sql = @"
SELECT DISTINCT
  TRIM(t.AIRLINE_CODE)          AS AirlineCode,
  TRIM(t.FLIGHT_NUMBER)         AS FlightNumber,
  TRIM(t.SERVICE_START_CITY)    AS DepCity,
  TRIM(t.SERVICE_END_CITY)      AS ArrCity,
  t.SERVICE_START_DATE          AS SchDepDate,
  t.SERVICE_START_TIME          AS SchDepTime
FROM vw_pax_details t
WHERE TRUNC(t.SERVICE_START_DATE) BETWEEN :fromDate AND :toDate
  AND TRIM(t.AIRLINE_CODE) = 'GF'
ORDER BY SchDepDate, FlightNumber";

            var args = new
            {
                fromDate = from.ToDateTime(TimeOnly.MinValue),
                toDate = to.ToDateTime(TimeOnly.MinValue)
            };

            var rows = await conn.QueryAsync<CddFlightRow>(new CommandDefinition(sql, args, cancellationToken: ct));
            var now = DateTime.UtcNow;
            var list = new List<FlightStatusDto>();

            _logger.LogInformation("Retrieved {FlightCount} flights from CDD for date range {From} to {To}", 
                rows.Count(), from, to);

            // Process flights in parallel to fetch OAG data
            var tasks = rows.Select(async r =>
            {
                try
                {
                    var flightDate = DateOnly.FromDateTime(r.SchDepDate ?? DateTime.Today);
                    var oagData = await _oagService.GetFlightStatusAsync(r.FlightNumber, flightDate, ct);
                    return (CddData: r, OagData: oagData, Success: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get OAG data for flight {FlightNumber} on {Date}", 
                        r.FlightNumber, r.SchDepDate);
                    return (CddData: r, OagData: (OagFlightInstance?)null, Success: false);
                }
            });

            var results = await Task.WhenAll(tasks);
            var successfulOagCalls = results.Count(r => r.Success && r.OagData != null);
            
            _logger.LogInformation("OAG enrichment completed: {SuccessCount}/{TotalCount} flights enriched", 
                successfulOagCalls, results.Length);

            foreach (var (cddData, oagData, _) in results)
            {
                var flight = ($"{cddData.AirlineCode} {cddData.FlightNumber}").Trim();
                string schDepTime = NormalizeTime(cddData.SchDepTime);
                string schDepDate = FormatDate(cddData.SchDepDate);

                var enrichedFlight = EnrichFlightWithOagData(cddData, oagData, schDepTime, schDepDate, now);
                list.Add(enrichedFlight);
            }

            return list.OrderBy(f => f.Departure.ScheduledDate).ThenBy(f => f.FlightNumber).ToList();
        }

        private FlightStatusDto EnrichFlightWithOagData(CddFlightRow cddData, OagFlightInstance? oagData,
            string schDepTime, string schDepDate, DateTime now)
        {
            var flight = ($"{cddData.AirlineCode} {cddData.FlightNumber}").Trim();

            // Extract OAG status information
            var statusDetail = oagData?.StatusDetails?.FirstOrDefault();
            var isDelayed = false;
            var status = "Scheduled";
            var statusWithTime = "Scheduled";

            if (statusDetail != null)
            {
                status = statusDetail.State;

                // Determine if flight is delayed
                var depActualTime = statusDetail.Departure?.ActualTime;
                var depEstimatedTime = statusDetail.Departure?.EstimatedTime;

                if (depActualTime?.OutGateTimeliness == "Delayed" || depEstimatedTime?.OutGateTimeliness == "Delayed")
                {
                    isDelayed = true;
                    var variation = depActualTime?.OutGateVariation ?? depEstimatedTime?.OutGateVariation ?? "";
                    statusWithTime = $"Delayed {variation}";
                }
                else if (status == "InGate" || status == "Arrived")
                {
                    statusWithTime = "Arrived";
                }
                else if (status == "Airborne" || status == "InFlight")
                {
                    statusWithTime = "In Flight";
                }
                else
                {
                    statusWithTime = status;
                }
            }

            // Build departure info
            var departure = new FlightPortInfo(
                City: Safe(cddData.DepCity),
                Airport: oagData?.Departure?.Airport?.Iata ?? string.Empty,
                Terminal: ParseTerminal(oagData?.Departure?.Terminal),
                ScheduledTime: schDepTime,
                ScheduledDate: schDepDate,
                EstimatedTime: ExtractTime(statusDetail?.Departure?.EstimatedTime?.OutGate?.Local),
                EstimatedDate: ExtractDate(statusDetail?.Departure?.EstimatedTime?.OutGate?.Local),
                ActualTime: ExtractTime(statusDetail?.Departure?.ActualTime?.OutGate?.Local),
                ActualDate: ExtractDate(statusDetail?.Departure?.ActualTime?.OutGate?.Local),
                CheckinCounter: string.Empty, // Not available in OAG
                Gate: statusDetail?.Departure?.Gate ?? string.Empty,
                Baggage: string.Empty // Not available for departure in OAG
            );

            // Build arrival info
            var arrScheduledTime = ExtractTime(oagData?.Arrival?.Time?.Local);
            var arrScheduledDate = ExtractDate(oagData?.Arrival?.Date?.Local);

            var arrival = new FlightPortInfo(
                City: Safe(cddData.ArrCity),
                Airport: oagData?.Arrival?.Airport?.Iata ?? string.Empty,
                Terminal: ParseTerminal(statusDetail?.Arrival?.ActualTerminal ?? oagData?.Arrival?.Terminal),
                ScheduledTime: arrScheduledTime,
                ScheduledDate: arrScheduledDate,
                EstimatedTime: ExtractTime(statusDetail?.Arrival?.EstimatedTime?.InGate?.Local),
                EstimatedDate: ExtractDate(statusDetail?.Arrival?.EstimatedTime?.InGate?.Local),
                ActualTime: ExtractTime(statusDetail?.Arrival?.ActualTime?.InGate?.Local),
                ActualDate: ExtractDate(statusDetail?.Arrival?.ActualTime?.InGate?.Local),
                CheckinCounter: string.Empty, // Not relevant for arrival
                Gate: statusDetail?.Arrival?.Gate ?? string.Empty,
                Baggage: statusDetail?.Arrival?.Baggage ?? string.Empty
            );

            return new FlightStatusDto(
                Flight: flight,
                FlightNumber: cddData.FlightNumber,
                AirlineCode: cddData.AirlineCode,
                Departure: departure,
                Arrival: arrival,
                Status: status,
                Delayed: isDelayed,
                StatusWithTime: statusWithTime,
                CurrentTime: now.ToString("HH:mm"),
                CurrentDate: now.ToString("dd/MM/yyyy"),
                CurrentDateTime: now
            );
        }

        private static int? ParseTerminal(string? terminal)
        {
            if (string.IsNullOrWhiteSpace(terminal)) return null;

            // Extract number from terminal string like "T1", "Terminal 1", etc.
            var digits = new string(terminal.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var terminalNumber) ? terminalNumber : null;
        }

        private static string ExtractTime(string? dateTimeString)
        {
            if (string.IsNullOrWhiteSpace(dateTimeString)) return string.Empty;

            try
            {
                if (DateTime.TryParse(dateTimeString, out var dt))
                {
                    return dt.ToString("HH:mm");
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return string.Empty;
        }

        private static string ExtractDate(string? dateTimeString)
        {
            if (string.IsNullOrWhiteSpace(dateTimeString)) return string.Empty;

            try
            {
                if (DateTime.TryParse(dateTimeString, out var dt))
                {
                    return dt.ToString("dd/MM/yyyy");
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return string.Empty;
        }

        private static string Safe(string? v) => string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim();
        private static string NormalizeTime(string? t)
        {
            var s = Safe(t);
            if (s.Length >= 5) return s[..5];
            if (TimeSpan.TryParse(s, out var ts)) return ts.ToString(@"hh\:mm");
            return string.Empty;
        }
        private static string FormatDate(DateTime? d) => d.HasValue ? d.Value.ToString("dd/MM/yyyy") : string.Empty;

        private static (string given, string surname) SplitNameSurnameFirst(string? full)
        {
            if (string.IsNullOrWhiteSpace(full)) return ("", "");
            var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return (parts[0], "");
            var surname = parts[0];
            var given = string.Join(" ", parts.Skip(1));
            return (given, surname);
        }

        // Update the constructor to accept IHttpClientFactory and IConfiguration
        public CddRepository(
            Func<string, IDbConnection> dbFactory,
            IOagService oagService,
            ILogger<CddRepository> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _db = dbFactory;
            _oagService = oagService;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }
        private sealed record CddBookedRow(string Pnr, string FullName, string? ClassOfService, string? TicketNumber, string? PhoneNumber, string FlightNumber, DateTime? ServiceStartDate);
        private sealed record CddFlightRow(
            string AirlineCode,
            string FlightNumber,
            string? DepCity,
            string? ArrCity,
            DateTime? SchDepDate,
            string? SchDepTime
        );
    }
}
