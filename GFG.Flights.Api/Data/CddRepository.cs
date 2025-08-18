using Dapper;
using System.Data;
using Oracle.ManagedDataAccess.Client;
using GFG.Flights.Api.Models;

namespace GFG.Flights.Api.Data
{
    public class CddRepository : ICddRepository
    {
        private readonly Func<string, IDbConnection> _db;
        public CddRepository(Func<string, IDbConnection> dbFactory) => _db = dbFactory;

        public async Task<IReadOnlyList<FlightDto>> GetTodaysFlightsAsync(CancellationToken ct)
        {
            using var conn = (OracleConnection)_db("CDD");
            const string sql = @"
SELECT DISTINCT
  TRIM(t.FLIGHT_NUMBER) AS FlightNumber,
  TRIM(t.SERVICE_START_CITY) AS Departure,
  TRIM(t.SERVICE_END_CITY)   AS Arrival,
  -- Combine date + time into a single timestamp (local schedule)
  TO_DATE(TO_CHAR(t.SERVICE_START_DATE,'YYYY-MM-DD') || ' ' || t.SERVICE_START_TIME,
          'YYYY-MM-DD HH24:MI:SS') AS StdUtc
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
  TRIM(t.TICKET_NUMBER)    AS TicketNumber
FROM vw_pax_details t
WHERE TRIM(t.FLIGHT_NUMBER) = :flightNumber
  AND TRUNC(t.SERVICE_START_DATE) = :flightDate
  AND (t.PASSENGER_STATUS IS NULL OR UPPER(t.PASSENGER_STATUS) IN ('BOOKED','CONFIRMED'))
ORDER BY t.PASSENGER_NAME";

            var args = new
            {
                flightNumber = flightNumber.Trim(),
                flightDate = date.ToDateTime(TimeOnly.MinValue)
            };

            // Pull raw rows, then split the name into (Surname, Given) — sample shows "SURNAME GIVEN ..."
            var raw = await conn.QueryAsync<CddBookedRow>(new CommandDefinition(sql, args, cancellationToken: ct));
            var result = raw.Select(r =>
            {
                var (given, surname) = SplitNameSurnameFirst(r.FullName);
                return new PassengerDto(r.Pnr, given, surname, Seat: null);
            }).ToList();

            return result;
        }
        private static (string given, string surname) SplitNameSurnameFirst(string? full)
        {
            if (string.IsNullOrWhiteSpace(full)) return ("", "");
            var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return (parts[0], "");               // unknown order, best effort
            var surname = parts[0];
            var given = string.Join(" ", parts.Skip(1));
            return (given, surname);
        }
        private sealed record CddBookedRow(string Pnr, string FullName, string? ClassOfService, string? TicketNumber);

    }


}
