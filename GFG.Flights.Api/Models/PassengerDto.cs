namespace GFG.Flights.Api.Models;

public record PassengerDto(
    string Pnr, 
    string GivenName, 
    string Surname, 
    string? SeatOrPhone,
    string? FlightNumber = null,
    DateOnly? FlightDate = null);
