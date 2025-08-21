using System.Text.Json.Serialization;

namespace GFG.Flights.Api.Models;

public sealed record OagResponse(
    [property: JsonPropertyName("data")] List<OagFlightInstance> Data,
    [property: JsonPropertyName("paging")] OagPaging Paging
);

public sealed record OagPaging(
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("totalPages")] int TotalPages,
    [property: JsonPropertyName("next")] string Next
);

public sealed record OagFlightInstance(
    [property: JsonPropertyName("carrier")] OagCarrier Carrier,
    [property: JsonPropertyName("serviceSuffix")] string ServiceSuffix,
    [property: JsonPropertyName("flightNumber")] int FlightNumber,
    [property: JsonPropertyName("sequenceNumber")] int SequenceNumber,
    [property: JsonPropertyName("flightType")] string FlightType,
    [property: JsonPropertyName("departure")] OagDeparture Departure,
    [property: JsonPropertyName("arrival")] OagArrival Arrival,
    [property: JsonPropertyName("elapsedTime")] int ElapsedTime,
    [property: JsonPropertyName("cargoTonnage")] double CargoTonnage,
    [property: JsonPropertyName("aircraftType")] OagAircraftType AircraftType,
    [property: JsonPropertyName("serviceType")] OagServiceType ServiceType,
    [property: JsonPropertyName("segmentInfo")] OagSegmentInfo SegmentInfo,
    [property: JsonPropertyName("distance")] OagDistance Distance,
    [property: JsonPropertyName("codeshare")] OagCodeshare Codeshare,
    [property: JsonPropertyName("scheduleInstanceKey")] string ScheduleInstanceKey,
    [property: JsonPropertyName("statusKey")] string StatusKey,
    [property: JsonPropertyName("statusDetails")] List<OagStatusDetail>? StatusDetails
);

public sealed record OagCarrier(
    [property: JsonPropertyName("iata")] string Iata,
    [property: JsonPropertyName("icao")] string Icao
);

public sealed record OagDeparture(
    [property: JsonPropertyName("airport")] OagAirport Airport,
    [property: JsonPropertyName("terminal")] string Terminal,
    [property: JsonPropertyName("country")] OagCountry Country,
    [property: JsonPropertyName("date")] OagDate Date,
    [property: JsonPropertyName("time")] OagTime Time
);

public sealed record OagArrival(
    [property: JsonPropertyName("airport")] OagAirport Airport,
    [property: JsonPropertyName("terminal")] string Terminal,
    [property: JsonPropertyName("country")] OagCountry Country,
    [property: JsonPropertyName("date")] OagDate Date,
    [property: JsonPropertyName("time")] OagTime Time
);

public sealed record OagAirport(
    [property: JsonPropertyName("iata")] string Iata,
    [property: JsonPropertyName("icao")] string Icao
);

public sealed record OagCountry(
    [property: JsonPropertyName("code")] string Code
);

public sealed record OagDate(
    [property: JsonPropertyName("local")] string Local,
    [property: JsonPropertyName("utc")] string Utc
);

public sealed record OagTime(
    [property: JsonPropertyName("local")] string Local,
    [property: JsonPropertyName("utc")] string Utc
);

public sealed record OagAircraftType(
    [property: JsonPropertyName("iata")] string Iata,
    [property: JsonPropertyName("icao")] string Icao
);

public sealed record OagServiceType(
    [property: JsonPropertyName("iata")] string Iata
);

public sealed record OagSegmentInfo(
    [property: JsonPropertyName("numberOfStops")] int NumberOfStops,
    [property: JsonPropertyName("intermediateAirports")] OagIntermediateAirports IntermediateAirports
);

public sealed record OagIntermediateAirports(
    [property: JsonPropertyName("iata")] List<string> Iata
);

public sealed record OagDistance(
    [property: JsonPropertyName("accumulatedGreatCircleKilometers")] double AccumulatedGreatCircleKilometers,
    [property: JsonPropertyName("accumulatedGreatCircleMiles")] double AccumulatedGreatCircleMiles,
    [property: JsonPropertyName("accumulatedGreatCircleNauticalMiles")] double AccumulatedGreatCircleNauticalMiles,
    [property: JsonPropertyName("greatCircleKilometers")] double GreatCircleKilometers,
    [property: JsonPropertyName("greatCircleMiles")] double GreatCircleMiles,
    [property: JsonPropertyName("greatCircleNauticalMiles")] double GreatCircleNauticalMiles
);

public sealed record OagCodeshare(
    [property: JsonPropertyName("jointOperationAirlineDesignators")] List<string> JointOperationAirlineDesignators,
    [property: JsonPropertyName("marketingFlights")] List<object> MarketingFlights
);

public sealed record OagStatusDetail(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("updatedAt")] string UpdatedAt,
    [property: JsonPropertyName("equipment")] OagEquipment? Equipment,
    [property: JsonPropertyName("departure")] OagStatusDeparture? Departure,
    [property: JsonPropertyName("arrival")] OagStatusArrival? Arrival
);

public sealed record OagEquipment(
    [property: JsonPropertyName("aircraftRegistrationNumber")] string AircraftRegistrationNumber,
    [property: JsonPropertyName("actualAircraftType")] OagAircraftType ActualAircraftType
);

public sealed record OagStatusDeparture(
    [property: JsonPropertyName("estimatedTime")] OagEstimatedTime? EstimatedTime,
    [property: JsonPropertyName("actualTime")] OagActualTime? ActualTime,
    [property: JsonPropertyName("airport")] OagAirport Airport,
    [property: JsonPropertyName("gate")] string? Gate,
    [property: JsonPropertyName("country")] OagCountry Country
);

public sealed record OagStatusArrival(
    [property: JsonPropertyName("estimatedTime")] OagEstimatedTime? EstimatedTime,
    [property: JsonPropertyName("actualTime")] OagActualTime? ActualTime,
    [property: JsonPropertyName("airport")] OagAirport Airport,
    [property: JsonPropertyName("actualTerminal")] string? ActualTerminal,
    [property: JsonPropertyName("gate")] string? Gate,
    [property: JsonPropertyName("baggage")] string? Baggage,
    [property: JsonPropertyName("country")] OagCountry Country
);

public sealed record OagEstimatedTime(
    [property: JsonPropertyName("outGateTimeliness")] string? OutGateTimeliness,
    [property: JsonPropertyName("outGateVariation")] string? OutGateVariation,
    [property: JsonPropertyName("outGate")] OagTimeDetail? OutGate,
    [property: JsonPropertyName("offGround")] OagTimeDetail? OffGround,
    [property: JsonPropertyName("inGateTimeliness")] string? InGateTimeliness,
    [property: JsonPropertyName("inGateVariation")] string? InGateVariation,
    [property: JsonPropertyName("onGround")] OagTimeDetail? OnGround,
    [property: JsonPropertyName("inGate")] OagTimeDetail? InGate
);

public sealed record OagActualTime(
    [property: JsonPropertyName("outGateTimeliness")] string? OutGateTimeliness,
    [property: JsonPropertyName("outGateVariation")] string? OutGateVariation,
    [property: JsonPropertyName("outGate")] OagTimeDetail? OutGate,
    [property: JsonPropertyName("offGround")] OagTimeDetail? OffGround,
    [property: JsonPropertyName("inGateTimeliness")] string? InGateTimeliness,
    [property: JsonPropertyName("inGateVariation")] string? InGateVariation,
    [property: JsonPropertyName("onGround")] OagTimeDetail? OnGround,
    [property: JsonPropertyName("inGate")] OagTimeDetail? InGate
);

public sealed record OagTimeDetail(
    [property: JsonPropertyName("local")] string Local,
    [property: JsonPropertyName("utc")] string Utc
);