using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;

namespace Padel.Api.Controllers;

// Reportes mensuales: facturación, ocupación por hora y top clientes. Es
// admin-only en todo, igual que PriceRules (el empleado no ve reportes).
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "admin")]
public class ReportsController : ControllerBase
{
    private const int OpenHour = 8;
    private const int CloseHour = 24;

    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("monthly")]
    public async Task<IActionResult> GetMonthly([FromQuery] int year, [FromQuery] int month)
    {
        if (month < 1 || month > 12)
            return BadRequest(new { error = "month debe estar entre 1 y 12" });

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = new DateOnly(year, month, daysInMonth);

        var bookings = await _db.Bookings
            .Where(b => b.Status == "confirmed" && b.Date >= firstDay && b.Date <= lastDay)
            .ToListAsync();

        var totalRevenue = bookings.Sum(b => b.TotalAmount);

        var courtsCount = await _db.Courts.CountAsync();
        var slotsPerHour = courtsCount * daysInMonth;

        var byHour = Enumerable.Range(OpenHour, CloseHour - OpenHour)
            .Select(hour =>
            {
                var occupied = bookings.Count(b => b.StartHour <= hour && hour < b.EndHour);
                var occupancyRate = slotsPerHour > 0 ? (decimal)occupied / slotsPerHour : 0m;
                return new { hour, occupancyRate };
            });

        var topCustomers = bookings
            .GroupBy(b => b.CustomerName)
            .Select(g => new
            {
                name = g.Key,
                totalSpent = g.Sum(b => b.TotalAmount),
                bookingsCount = g.Count(),
            })
            .OrderByDescending(c => c.totalSpent);

        return Ok(new
        {
            totalRevenue,
            byHour,
            topCustomers,
        });
    }
}
