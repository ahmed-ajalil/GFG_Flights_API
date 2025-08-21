using Microsoft.AspNetCore.Mvc;
using GFG.Flights.Api.Models;
using GFG.Flights.Api.Data;
using System.Threading.Tasks;

namespace GFG.Flights.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FlightsController : ControllerBase
{
    private readonly ICddRepository _cdd;
    private readonly IAirportRepository _airport;
    public FlightsController(ICddRepository cdd, IAirportRepository airport)
    {
        _cdd = cdd;
        _airport = airport;
    }
    // Changed: accepts from/to date range and returns enriched structure
    [HttpGet("today")]
    public async Task<ActionResult<IEnumerable<FlightStatusDto>>> GetFlights([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _cdd.GetFlightsAsync(from, to, ct));

    [HttpGet("{flightNumber}/{date}/booked")]
    public async Task<ActionResult<IEnumerable<PassengerDto>>> GetBooked(
        string flightNumber, 
        DateOnly date, CancellationToken ct)
        => Ok(await _cdd.GetBookedPassengersAsync(flightNumber, date, ct));
    [HttpGet("{flightNumber}/{date}/checked-in")]
    public async Task<ActionResult<IEnumerable<PassengerDto>>> GetCheckedIn(
        string flightNumber, 
        DateOnly date, CancellationToken ct)
        => Ok(await _airport.GetCheckedInPassengersAsync(flightNumber, date, ct));
    [HttpGet("{flightNumber}/{date}/boarded")]
    public async Task<ActionResult<IEnumerable<PassengerDto>>> GetBoarded(
       string flightNumber,
       DateOnly date, CancellationToken ct)
       => Ok(await _airport.GetBoardedPassengersAsync(flightNumber, date, ct));
}