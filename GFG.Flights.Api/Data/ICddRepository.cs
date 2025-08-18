using GFG.Flights.Api.Models;

namespace GFG.Flights.Api.Data

{
    public interface ICddRepository
    {
        Task<IReadOnlyList<FlightDto>> GetTodaysFlightsAsync(CancellationToken ct);
        Task<IReadOnlyList<PassengerDto>> GetBookedPassengersAsync(string flightNumber, DateOnly date, CancellationToken ct);
    }
}
