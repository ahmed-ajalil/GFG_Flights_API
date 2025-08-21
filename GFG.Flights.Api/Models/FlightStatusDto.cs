namespace GFG.Flights.Api.Models;

public sealed record FlightPortInfo(
    string City,
    string Airport,
    int? Terminal,
    string ScheduledTime,
    string ScheduledDate,
    string EstimatedTime,
    string EstimatedDate,
    string ActualTime,
    string ActualDate,
    string CheckinCounter,
    string Gate,
    string Baggage
);

public sealed record FlightStatusDto(
    string Flight,
    string FlightNumber,
    string AirlineCode,
    FlightPortInfo Departure,
    FlightPortInfo Arrival,
    string Status,
    bool Delayed,
    string StatusWithTime,
    string CurrentTime,
    string CurrentDate,
    DateTime CurrentDateTime
);
