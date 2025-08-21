# OAG API Integration

This document describes the OAG (Official Airline Guide) API integration for enriching flight data.

## Overview

The OAG integration enhances the flight data returned by the `/api/flights/today` endpoint by fetching real-time flight status information from the OAG API. This includes:

- Airport codes (IATA)
- Terminal information
- Gate assignments
- Baggage carousel information
- Estimated and actual departure/arrival times
- Flight status (Scheduled, Delayed, In Flight, Arrived, etc.)
- Delay information and variations

## Configuration

Add the following configuration to your `appsettings.json` or Azure App Service Configuration:

```json
{
  "OagSubscriptionKey": "your-oag-subscription-key-here"
}
```

## API Usage

The OAG API is called automatically when fetching flights through the `/api/flights/today` endpoint. For each flight found in the CDD database, the system will:

1. Make a parallel call to the OAG API using the flight number and date
2. Enrich the flight data with information from OAG
3. Return the combined data structure

## OAG API Endpoint

The integration uses the OAG Flight Instances API:
```
GET https://api.oag.com/flight-instances/?DepartureDateTime={date}&CarrierCode=GF&FlightNumber={number}&Content=status,map&CodeType=IATA&version=v2
```

## Enhanced Data Fields

The following fields are now populated from OAG data when available:

### Departure
- `airport`: IATA airport code (e.g., "LHR", "BAH")
- `terminal`: Terminal number (e.g., 1, 2)
- `estimatedTime`: Estimated departure time
- `estimatedDate`: Estimated departure date
- `actualTime`: Actual departure time
- `actualDate`: Actual departure date
- `gate`: Departure gate

### Arrival
- `airport`: IATA airport code
- `terminal`: Terminal number
- `scheduledTime`: Scheduled arrival time
- `scheduledDate`: Scheduled arrival date
- `estimatedTime`: Estimated arrival time
- `estimatedDate`: Estimated arrival date
- `actualTime`: Actual arrival time
- `actualDate`: Actual arrival date
- `gate`: Arrival gate
- `baggage`: Baggage carousel

### Flight Status
- `status`: Current flight status (InGate, Airborne, Arrived, etc.)
- `delayed`: Boolean indicating if flight is delayed
- `statusWithTime`: Status with delay information

## Error Handling

The OAG integration includes robust error handling:

- If the OAG API is unavailable, flights will still be returned with CDD data only
- Failed OAG calls are logged but don't prevent the response
- Individual flight OAG failures don't affect other flights in the same request
- Network timeouts are handled gracefully (30-second timeout)

## Performance

- OAG API calls are made in parallel for all flights in a request
- Each flight gets its own OAG lookup to ensure accurate real-time data
- Failed lookups don't block successful ones
- Caching is not implemented to ensure real-time accuracy

## Logging

The OAG service includes detailed logging:

```json
{
  "Logging": {
    "LogLevel": {
      "GFG.Flights.Api.Services.OagService": "Debug"
    }
  }
}
```

This will log:
- API calls made to OAG
- Success/failure of individual flight lookups
- Errors and exceptions during API calls

## Example Response

Before OAG integration:
```json
{
  "departure": {
    "city": "LHR",
    "airport": "",
    "terminal": null,
    "gate": "",
    "scheduledTime": "10:00"
  }
}
```

After OAG integration:
```json
{
  "departure": {
    "city": "LHR",
    "airport": "LHR",
    "terminal": 1,
    "gate": "36",
    "scheduledTime": "10:00",
    "estimatedTime": "10:05",
    "actualTime": "10:09"
  }
}
```