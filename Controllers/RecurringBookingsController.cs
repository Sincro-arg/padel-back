using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

// Turno fijo semanal. Ambos roles pueden cargarlo, igual que una reserva
// suelta (no hay [Authorize(Roles = "admin")] a nivel de clase).
[ApiController]
[Route("api/recurring-bookings")]
[Authorize]
public class RecurringBookingsController : ControllerBase
{
    private const int WeeksToGenerate = 8;

    private readonly AppDbContext _db;

    public RecurringBookingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var recurrences = await _db.RecurringBookings
            .OrderBy(r => r.Weekday).ThenBy(r => r.StartHour)
            .ToListAsync();

        return Ok(recurrences.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RecurringBookingDto dto)
    {
        var error = await ValidateAsync(dto);
        if (error != null) return BadRequest(new { error });

        var recurring = new RecurringBooking
        {
            CourtId = dto.CourtId,
            CustomerName = dto.CustomerName.Trim(),
            CustomerPhone = dto.CustomerPhone.Trim(),
            Weekday = dto.Weekday,
            StartHour = dto.StartHour,
            EndHour = dto.EndHour,
            MemberId = dto.MemberId,
        };
        _db.RecurringBookings.Add(recurring);

        // Genera las reservas concretas de las próximas 8 semanas. Cada una cae en
        // una fecha distinta (misma semana no se repite), así que no pueden
        // solaparse entre ellas: el chequeo de solapamiento solo mira reservas ya
        // persistidas de otro origen. Si una semana puntual ya tiene algo cargado
        // ahí, o no hay tarifa configurada para ese horario, esa semana se salta
        // en vez de abortar todo el turno fijo.
        var firstOccurrence = FirstOccurrence(dto.Weekday);
        for (var week = 0; week < WeeksToGenerate; week++)
        {
            var date = firstOccurrence.AddDays(7 * week);

            if (await BookingPricing.OverlapsAsync(_db, dto.CourtId, date, dto.StartHour, dto.EndHour, excludeId: null))
                continue;

            var (total, priceError) = await BookingPricing.CalculatePriceAsync(_db, date, dto.StartHour, dto.EndHour, dto.MemberId);
            if (priceError != null) continue;

            _db.Bookings.Add(new Booking
            {
                CourtId = dto.CourtId,
                CustomerName = recurring.CustomerName,
                CustomerPhone = recurring.CustomerPhone,
                Date = date,
                StartHour = dto.StartHour,
                EndHour = dto.EndHour,
                MemberId = dto.MemberId,
                Status = "confirmed",
                TotalAmount = total!.Value,
                PaidAmount = 0,
                PaymentStatus = "pending",
                IsRecurring = true,
                RecurringBookingId = recurring.Id,
            });
        }

        await _db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, ToDto(recurring));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var recurring = await _db.RecurringBookings.FirstOrDefaultAsync(r => r.Id == id);
        if (recurring == null) return NotFound(new { error = "Turno fijo no encontrado" });

        var today = DateOnly.FromDateTime(DateTime.Now);
        var futureBookings = await _db.Bookings
            .Where(b => b.RecurringBookingId == id && b.Status == "confirmed" && b.Date >= today)
            .ToListAsync();
        foreach (var booking in futureBookings)
            booking.Status = "cancelled";

        _db.RecurringBookings.Remove(recurring);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // Primer día >= hoy cuyo DayOfWeek coincide con el weekday pedido (0=domingo…6=sábado,
    // igual que System.DayOfWeek).
    private static DateOnly FirstOccurrence(int weekday)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var diff = (weekday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(diff);
    }

    private async Task<string?> ValidateAsync(RecurringBookingDto? dto)
    {
        if (dto == null) return "Datos inválidos";
        if (string.IsNullOrWhiteSpace(dto.CustomerName)) return "El nombre del cliente es requerido";
        if (string.IsNullOrWhiteSpace(dto.CustomerPhone)) return "El teléfono del cliente es requerido";
        if (dto.Weekday < 0 || dto.Weekday > 6) return "weekday debe estar entre 0 (domingo) y 6 (sábado)";
        if (dto.StartHour < 8 || dto.StartHour > 24 || dto.EndHour < 8 || dto.EndHour > 24)
            return "Las horas deben estar entre 8 y 24";
        if (dto.StartHour >= dto.EndHour) return "startHour debe ser menor a endHour";

        var courtExists = await _db.Courts.AnyAsync(c => c.Id == dto.CourtId);
        if (!courtExists) return "La cancha indicada no existe";

        return null;
    }

    private static object ToDto(RecurringBooking r) => new
    {
        id = r.Id,
        courtId = r.CourtId,
        customerName = r.CustomerName,
        customerPhone = r.CustomerPhone,
        weekday = r.Weekday,
        startHour = r.StartHour,
        endHour = r.EndHour,
        memberId = r.MemberId,
    };
}

public class RecurringBookingDto
{
    public Guid CourtId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int Weekday { get; set; }
    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public Guid? MemberId { get; set; }
}
