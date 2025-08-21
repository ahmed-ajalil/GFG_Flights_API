using Dapper;
using System.Data;
using Oracle.ManagedDataAccess.Client;
using GFG.Flights.Api.Models;

namespace GFG.Flights.Api.Data;

public sealed class AirportRepository : IAirportRepository
{
    private readonly Func<string, IDbConnection> _db;
    public AirportRepository(Func<string, IDbConnection> dbFactory) => _db = dbFactory;

    public async Task<IReadOnlyList<PassengerDto>> GetCheckedInPassengersAsync(string flightNumber, DateOnly date, CancellationToken ct)
    {
        using var conn = (OracleConnection)_db("AirportDb");

        // Normalize flight number to 4 digits to match FLT_NR (e.g., "277" -> "0277")
        var normalized = NormalizeFlightNumber(flightNumber);

        const string sql = @"
SELECT
  TRIM(a.PNR)         AS Pnr,
  TRIM(a.PAX_NAME)    AS PaxName,
  TRIM(a.SEAT_NO)     AS Seat
FROM V_PAX_ALL_DETAILS a
WHERE TRIM(a.ALN_CD) = 'GF'
  AND TRIM(a.FLT_NR)  = :fltNr
  AND TRUNC(NVL(a.PUB_DEP_DT, a.SCH_DEP_DT)) = :flightDate
  AND UPPER(TRIM(a.STATUS)) IN ('CKIN') 
  AND BOARDED = 'FALSE'
ORDER BY a.PAX_NAME";

        var args = new
        {
            fltNr = normalized,
            flightDate = date.ToDateTime(TimeOnly.MinValue)
        };

        var raw = await conn.QueryAsync<AirportRow>(new CommandDefinition(sql, args, cancellationToken: ct));

        // Airport names are typically "GIVEN [MIDDLE] SURNAME"
        var result = raw.Select(r =>
        {
            var (given, surname) = SplitNameSurnameLast(r.PaxName);
            return new PassengerDto(r.Pnr, given, surname, r.Seat);
        }).ToList();

        return result;
    }
    public async Task<IReadOnlyList<PassengerDto>> GetBoardedPassengersAsync(string flightNumber, DateOnly date, CancellationToken ct)
    {
        using var conn = (OracleConnection)_db("AirportDb");

        // Normalize flight number to 4 digits to match FLT_NR (e.g., "277" -> "0277")
        var normalized = NormalizeFlightNumber(flightNumber);

        const string sql = @"
SELECT
  TRIM(a.PNR)         AS Pnr,
  TRIM(a.PAX_NAME)    AS PaxName,
  TRIM(a.SEAT_NO)     AS Seat
FROM V_PAX_ALL_DETAILS a
WHERE TRIM(a.ALN_CD) = 'GF'
  AND TRIM(a.FLT_NR)  = :fltNr
  AND TRUNC(NVL(a.PUB_DEP_DT, a.SCH_DEP_DT)) = :flightDate
  AND UPPER(TRIM(a.BOARDED)) = 'TRUE'
      
ORDER BY a.PAX_NAME";

        var args = new
        {
            fltNr = normalized,
            flightDate = date.ToDateTime(TimeOnly.MinValue)
        };

        var raw = await conn.QueryAsync<AirportRow>(new CommandDefinition(sql, args, cancellationToken: ct));

        // Airport names are typically "GIVEN [MIDDLE] SURNAME"
        var result = raw.Select(r =>
        {
            var (given, surname) = SplitNameSurnameLast(r.PaxName);
            return new PassengerDto(r.Pnr, given, surname, r.Seat);
        }).ToList();

        return result;
    }
    private static string NormalizeFlightNumber(string flightNumber)
    {
        var digits = new string((flightNumber ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return "0000";
        return digits.Length >= 4 ? digits[^4..] : digits.PadLeft(4, '0');
    }

    private sealed record AirportRow(string Pnr, string PaxName, string? Seat);

    // e.g., "MOHAMMAD BARKATH ALI" => Given="MOHAMMAD BARKATH", Surname="ALI"
    private static (string given, string surname) SplitNameSurnameLast(string? full)
    {
        if (string.IsNullOrWhiteSpace(full)) return ("", "");
        var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return (parts[0], "");
        var surname = parts[^1];
        var given = string.Join(" ", parts.Take(parts.Length - 1));
        return (given, surname);
    }
}
