using GFG.Flights.Api.Models;

namespace GFG.Flights.Api.Data

{
    public interface IAirportRepository
    {
        Task<IReadOnlyList<PassengerDto>> GetCheckedInPassengersAsync(string flightNumber, DateOnly date, CancellationToken ct);
        Task<IReadOnlyList<PassengerDto>> GetBoardedPassengersAsync(string flightNumber, DateOnly date, CancellationToken ct);
    }
}
