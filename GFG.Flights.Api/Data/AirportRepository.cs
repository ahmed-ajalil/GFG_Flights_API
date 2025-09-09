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
    public async Task<IReadOnlyList<CustomerFlightDetailDto>> GetCustomerFlightDetailsAsync(
        string? customerName, DateOnly from, DateOnly to, string? docNumber, CancellationToken ct)
    {
        using var conn = (OracleConnection)_db("AirportDb");
        // FIX 1: Explicitly list columns instead of SELECT *. This is safer and more performant.
        // Note: Ensure these column names exactly match the properties of CustomerFlightDetailDto.
        const string sqlBase = @"
SELECT
    SEX AS Sex, PAX_NAME AS PaxName, FLT_SEQ_NR AS FltSeqNr, PAX_CLASS AS PaxClass, STATUS AS Status, PAX_TYPE AS PaxType, PNR AS Pnr, PAXNAT AS PaxNat,
    BOARDED AS Boarded, FFPNUM AS FfpNum, FLT_NR AS FltNr, ALN_CD AS AlnCd, LEG_SEQ_NR AS LegSeqNr, ACTUAL_DEP_ARP_CD AS ActualDepArpCd,
    DEP_STATION_NAME AS DepStationName, ACTUAL_ARV_ARP_CD AS ActualArvArpCd, ARV_STATION_NAME AS ArvStationName, SCH_DEP_DT AS SchDepDt,
    SCH_ARV_DT AS SchArvDt, PUB_DEP_DT AS PubDepDt, PUB_ARV_DT AS PubArvDt, ACTUAL_DEP_DT AS ActualDepDt, ACTUAL_ARV_DT AS ActualArvDt,
    CNCL_CD AS CnclCd, DISRUPTED_FLAG AS DisruptedFlag, FFP_FLAG AS FfpFlag, DEP_DT AS DepDt, SEAT_NO AS SeatNo, DOC_NUMBER AS DocNumber, M_CLS AS MCls,
    MANIFEST_TKT_NR AS ManifestTktNr, DESTINATION AS Destination, MANIFEST_DATE AS ManifestDate, DOB AS Dob, PAXDOC AS PaxDoc,
    PAXBOARDPOINT AS PaxBoardPoint, PASSENGER_PHONE AS PassengerPhone, PASSENGER_EMAIL AS PassengerEmail
FROM V_PAX_ALL_DETAILS
WHERE 1 = 1"; // Start with a tautology to simplify appending AND conditions.
        var conditions = new List<string>();
        var args = new DynamicParameters();
        // FIX 2: Use a SARGable date range condition for better index usage.
        // This avoids applying TRUNC() to the database column.
        conditions.Add("NVL(PUB_DEP_DT, SCH_DEP_DT) >= :fromDate AND NVL(PUB_DEP_DT, SCH_DEP_DT) < :toDatePlusOne");
        args.Add("fromDate", from.ToDateTime(TimeOnly.MinValue));
        args.Add("toDatePlusOne", to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        // FIX 3: Prevent Regex Injection. Use simple LIKE clauses for each word in the name.
        // This is safer and performs better than a complex regex.
        if (!string.IsNullOrWhiteSpace(customerName))
        {
            var nameParts = customerName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < nameParts.Length; i++)
            {
                var paramName = $"namePart{i}";
                conditions.Add("UPPER(PAX_NAME) LIKE :" + paramName);
                // NOTE: For best performance, create a function-based index on UPPER(PAX_NAME) in the database.
                args.Add(paramName, $"%{nameParts[i].ToUpper()}%");
            }
        }
        if (!string.IsNullOrWhiteSpace(docNumber))
        {
            conditions.Add("DOC_NUMBER = :docNumber");
            // FIX 4 (Minor): Add parameter names without the ':' prefix.
            args.Add("docNumber", docNumber);
        }
        var finalSql = sqlBase + " AND " + string.Join(" AND ", conditions) + " ORDER BY PAX_NAME";
        var result = await conn.QueryAsync<CustomerFlightDetailDto>(new CommandDefinition(finalSql, args, cancellationToken: ct));
        return result.ToList();
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
