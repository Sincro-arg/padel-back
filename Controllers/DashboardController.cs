using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

// Pantalla de inicio: "lo que quiero ver de un vistazo". Ambos roles la ven;
// el front oculta incomeToday/debtTotal si no es admin, así que el endpoint
// no restringe por rol (igual que BookingsController).
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
        var today = DateOnly.FromDateTime(DateTime.Now);
        var currentHour = DateTime.Now.Hour;

        var courts = await _db.Courts.OrderBy(c => c.Name).ToListAsync();

        var todayBookings = await _db.Bookings
            .Include(b => b.Court)
            .Where(b => b.Date == today)
            .OrderBy(b => b.StartHour)
            .ToListAsync();

        var courtsToday = courts.Select(c =>
        {
            var current = todayBookings.FirstOrDefault(b =>
                b.CourtId == c.Id && b.Status == "confirmed" && b.StartHour <= currentHour && currentHour < b.EndHour);

            return new
            {
                courtId = c.Id,
                courtName = c.Name,
                status = current != null ? "occupied" : "free",
                currentBooking = current != null ? ToBookingDto(current) : null,
            };
        });

        var bookingPayments = await _db.BookingPayments
            .Where(p => p.CreatedAt.Year == today.Year && p.CreatedAt.Month == today.Month && p.CreatedAt.Day == today.Day)
            .SumAsync(p => p.Amount);

        var productSales = await _db.ProductSales
            .Where(s => s.CreatedAt.Year == today.Year && s.CreatedAt.Month == today.Month && s.CreatedAt.Day == today.Day)
            .SumAsync(s => s.Amount);

        var incomeToday = bookingPayments + productSales;

        var allBookings = await _db.Bookings.ToListAsync();
        var debtTotal = allBookings.Sum(b => Math.Max(0, AmountDue(b)));

        var members = await _db.Members.ToListAsync();
        var memberPayments = await _db.MemberPayments.ToListAsync();
        var membersLate = members
            .Select(m => new { Member = m, MonthsOwed = MemberDebt.MonthsOwed(m, memberPayments.Where(p => p.MemberId == m.Id)) })
            .Where(x => x.MonthsOwed > 0)
            .OrderByDescending(x => x.MonthsOwed)
            .Select(x => new
            {
                id = x.Member.Id,
                name = x.Member.Name,
                monthsOwed = x.MonthsOwed,
            });

        return Ok(new
        {
            courts = courtsToday,
            todayBookings = todayBookings.Select(ToBookingDto),
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
