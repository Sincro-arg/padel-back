using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

// Pantalla de inicio: estado de las canchas ahora mismo, turnos de hoy,
// caja de hoy y socios atrasados. Ambos roles la ven; el front oculta los
// montos de caja al empleado, pero el back no discrimina acá.
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetToday()
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var currentHour = now.Hour;

        var courts = await _db.Courts.OrderBy(c => c.Name).ToListAsync();

        var todaysBookings = await _db.Bookings
            .Include(b => b.Court)
            .Where(b => b.Date == today)
            .OrderBy(b => b.StartHour)
            .ToListAsync();

        var courtsToday = courts.Select(c =>
        {
            var current = todaysBookings.FirstOrDefault(b =>
                b.CourtId == c.Id && b.Status == "confirmed" &&
                b.StartHour <= currentHour && currentHour < b.EndHour);

            return new
            {
                courtId = c.Id,
                courtName = c.Name,
                status = current != null ? "occupied" : "free",
                currentBooking = current == null ? null : ToBookingDto(current),
            };
        });

        var allBookings = await _db.Bookings.ToListAsync();
        var debtTotal = allBookings.Sum(b => Math.Max(0, AmountDue(b)));

        var bookingPaymentsToday = await _db.BookingPayments
            .Where(p => p.CreatedAt.Year == today.Year && p.CreatedAt.Month == today.Month && p.CreatedAt.Day == today.Day)
            .SumAsync(p => p.Amount);

        var productSalesToday = await _db.ProductSales
            .Where(s => s.CreatedAt.Year == today.Year && s.CreatedAt.Month == today.Month && s.CreatedAt.Day == today.Day)
            .SumAsync(s => s.Amount);

        var incomeToday = bookingPaymentsToday + productSalesToday;

        var members = await _db.Members.ToListAsync();
        var memberPayments = await _db.MemberPayments.ToListAsync();

        var membersLate = members
            .Select(m => new
            {
                m.Id,
                m.Name,
                MonthsOwed = MemberStatus.MonthsOwed(m.JoinedAt, memberPayments.Where(p => p.MemberId == m.Id).Select(p => (p.Year, p.Month))),
            })
            .Where(x => x.MonthsOwed >= 1)
            .OrderByDescending(x => x.MonthsOwed)
            .Select(x => new { id = x.Id, name = x.Name, monthsOwed = x.MonthsOwed });

        return Ok(new
        {
            courts = courtsToday,
            todayBookings = todaysBookings.Select(ToBookingDto),
            incomeToday,
            debtTotal,
            membersLate,
        });
    }

    private static decimal AmountDue(Booking b)
    {
        var due = b.Status == "cancelled" ? (b.CancellationFee ?? 0m) : b.TotalAmount;
        return due - b.PaidAmount;
    }

    private static object ToBookingDto(Booking b) => new
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
