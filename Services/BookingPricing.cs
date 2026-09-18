using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;

namespace Padel.Api.Services;

/// <summary>
/// Cálculo de precio por PriceRules y chequeo de solapamiento de horarios.
/// Lo usan BookingsController y RecurringBookingsController para que una
/// reserva generada por un turno fijo se cotice y valide exactamente igual
/// que una reserva cargada a mano.
/// </summary>
public static class BookingPricing
{
    public static async Task<bool> OverlapsAsync(AppDbContext db, Guid courtId, DateOnly date, int startHour, int endHour, Guid? excludeId)
    {
        return await db.Bookings.AnyAsync(b =>
            b.CourtId == courtId &&
            b.Date == date &&
            b.Status == "confirmed" &&
            b.Id != (excludeId ?? Guid.Empty) &&
            b.StartHour < endHour &&
            b.EndHour > startHour);
    }

    public static async Task<(decimal? total, string? error)> CalculatePriceAsync(AppDbContext db, DateOnly date, int startHour, int endHour, Guid? memberId = null)
    {
        var dayType = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday
            ? "weekend"
            : "weekday";

        var rules = await db.PriceRules.Where(r => r.DayType == dayType).ToListAsync();

        decimal total = 0;
        for (var hour = startHour; hour < endHour; hour++)
        {
            var rule = rules.FirstOrDefault(r => r.StartHour <= hour && hour < r.EndHour);
            if (rule == null)
                return (null, $"No hay una tarifa de precio configurada para las {hour}:00");
            total += rule.PricePerHour;
        }

        if (memberId.HasValue)
        {
            var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId.Value);
            if (member != null && member.DiscountPercent > 0)
                total -= total * (member.DiscountPercent / 100m);
        }

        return (total, null);
    }
}
