namespace GFG.Flights.Api.Models
{
    public record FlightDto(string FlightNumber, string Departure, string Arrival, DateTime StdUtc);
}
