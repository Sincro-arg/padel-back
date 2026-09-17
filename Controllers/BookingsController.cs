using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Controllers;

// Reservas: alta, edición, cancelación y baja (para corregir un error de carga).
// Ambos roles pueden cargar y cobrar turnos, por eso no hay [Authorize(Roles = "admin")]
// a nivel de clase.
[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public BookingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetByDate([FromQuery] string date)
    {
        if (!DateOnly.TryParse(date, out var day))
            return BadRequest(new { error = "Fecha inválida, formato esperado YYYY-MM-DD" });

        var bookings = await _db.Bookings
            .Include(b => b.Court)
            .Where(b => b.Date == day)
            .OrderBy(b => b.StartHour)
            .ToListAsync();

        return Ok(bookings.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BookingDto dto)
    {
        var (error, date) = await ValidateAsync(dto);
        if (error != null) return BadRequest(new { error });

        if (await OverlapsAsync(dto.CourtId, date, dto.StartHour, dto.EndHour, excludeId: null))
            return Conflict(new { error = "La cancha ya tiene una reserva en ese horario" });

        var (total, priceError) = await CalculatePriceAsync(date, dto.StartHour, dto.EndHour);
        if (priceError != null) return BadRequest(new { error = priceError });

        var booking = new Booking
        {
            CourtId = dto.CourtId,
            CustomerName = dto.CustomerName.Trim(),
            CustomerPhone = dto.CustomerPhone.Trim(),
            Date = date,
            StartHour = dto.StartHour,
            EndHour = dto.EndHour,
            MemberId = dto.MemberId,
            Status = "confirmed",
            TotalAmount = total!.Value,
            PaidAmount = 0,
            PaymentStatus = "pending",
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        await _db.Entry(booking).Reference(b => b.Court).LoadAsync();
        return StatusCode(StatusCodes.Status201Created, ToDto(booking));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] BookingDto dto)
    {
        var booking = await _db.Bookings.Include(b => b.Court).FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound(new { error = "Reserva no encontrada" });

        var (error, date) = await ValidateAsync(dto);
        if (error != null) return BadRequest(new { error });

        if (await OverlapsAsync(dto.CourtId, date, dto.StartHour, dto.EndHour, excludeId: id))
            return Conflict(new { error = "La cancha ya tiene una reserva en ese horario" });

        var (total, priceError) = await CalculatePriceAsync(date, dto.StartHour, dto.EndHour);
        if (priceError != null) return BadRequest(new { error = priceError });

        booking.CourtId = dto.CourtId;
        booking.CustomerName = dto.CustomerName.Trim();
        booking.CustomerPhone = dto.CustomerPhone.Trim();
        booking.Date = date;
        booking.StartHour = dto.StartHour;
        booking.EndHour = dto.EndHour;
        booking.TotalAmount = total!.Value;
        await _db.SaveChangesAsync();

        if (booking.CourtId != booking.Court?.Id)
            await _db.Entry(booking).Reference(b => b.Court).LoadAsync();

        return Ok(ToDto(booking));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var booking = await _db.Bookings.Include(b => b.Court).FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound(new { error = "Reserva no encontrada" });

        if (booking.Status == "cancelled")
            return BadRequest(new { error = "La reserva ya está cancelada" });

        var startsAt = booking.Date.ToDateTime(new TimeOnly(booking.StartHour % 24, 0));
        var hoursUntilStart = (startsAt - DateTime.Now).TotalHours;

        booking.Status = "cancelled";
        booking.CancellationFee = hoursUntilStart < 4 ? booking.TotalAmount * 0.5m : null;

        var due = booking.CancellationFee ?? 0m;
        booking.PaymentStatus = booking.PaidAmount <= 0
            ? (due > 0 ? "pending" : "paid")
            : booking.PaidAmount >= due ? "paid" : "partial";

        await _db.SaveChangesAsync();
        return Ok(ToDto(booking));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound(new { error = "Reserva no encontrada" });

        _db.Bookings.Remove(booking);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private async Task<bool> OverlapsAsync(Guid courtId, DateOnly date, int startHour, int endHour, Guid? excludeId)
    {
        return await _db.Bookings.AnyAsync(b =>
            b.CourtId == courtId &&
            b.Date == date &&
            b.Status == "confirmed" &&
            b.Id != (excludeId ?? Guid.Empty) &&
            b.StartHour < endHour &&
            b.EndHour > startHour);
    }

    private async Task<(decimal? total, string? error)> CalculatePriceAsync(DateOnly date, int startHour, int endHour)
    {
        var dayType = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday
            ? "weekend"
            : "weekday";

        var rules = await _db.PriceRules.Where(r => r.DayType == dayType).ToListAsync();

        decimal total = 0;
        for (var hour = startHour; hour < endHour; hour++)
        {
            var rule = rules.FirstOrDefault(r => r.StartHour <= hour && hour < r.EndHour);
            if (rule == null)
                return (null, $"No hay una tarifa de precio configurada para las {hour}:00");
            total += rule.PricePerHour;
        }
        return (total, null);
    }

    private async Task<(string? error, DateOnly date)> ValidateAsync(BookingDto? dto)
    {
        if (dto == null) return ("Datos inválidos", default);
        if (string.IsNullOrWhiteSpace(dto.CustomerName)) return ("El nombre del cliente es requerido", default);
        if (string.IsNullOrWhiteSpace(dto.CustomerPhone)) return ("El teléfono del cliente es requerido", default);
        if (!DateOnly.TryParse(dto.Date, out var date)) return ("Fecha inválida, formato esperado YYYY-MM-DD", default);
        if (dto.StartHour < 8 || dto.StartHour > 24 || dto.EndHour < 8 || dto.EndHour > 24)
            return ("Las horas deben estar entre 8 y 24", default);
        if (dto.StartHour >= dto.EndHour) return ("startHour debe ser menor a endHour", default);

        var courtExists = await _db.Courts.AnyAsync(c => c.Id == dto.CourtId);
        if (!courtExists) return ("La cancha indicada no existe", default);

        return (null, date);
    }

    private static object ToDto(Booking b) => new
    {
        id = b.Id,
        courtId = b.CourtId,
        courtName = b.Court?.Name,
        customerName = b.CustomerName,
        customerPhone = b.CustomerPhone,
        date = b.Date.ToString("yyyy-MM-dd"),
        startHour = b.StartHour,
        endHour = b.EndHour,
        memberId = b.MemberId,
        status = b.Status,
        totalAmount = b.TotalAmount,
        paidAmount = b.PaidAmount,
        paymentStatus = b.PaymentStatus,
        cancellationFee = b.CancellationFee,
        isRecurring = b.IsRecurring,
        recurringBookingId = b.RecurringBookingId,
    };
}

public class BookingDto
{
    public Guid CourtId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public Guid? MemberId { get; set; }
}
