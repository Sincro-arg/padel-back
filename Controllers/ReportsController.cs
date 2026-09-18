using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;

namespace Padel.Api.Controllers;

// Reportes mensuales: solo admin (el empleado no ve precios ni reportes).
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "admin")]
public class ReportsController : ControllerBase
{
    private const int OpenHour = 8;
    private const int CloseHour = 24;
    private const int TopCustomersLimit = 10;

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

        var courtsCount = await _db.Courts.CountAsync();
        var daysInMonth = DateTime.DaysInMonth(year, month);

        var bookings = await _db.Bookings
            .Where(b => b.Date.Year == year && b.Date.Month == month && b.Status == "confirmed")
            .ToListAsync();

        var totalRevenue = bookings.Sum(b => b.TotalAmount);

        var byHour = new List<object>();
        for (var hour = OpenHour; hour < CloseHour; hour++)
        {
            var occupied = bookings.Count(b => b.StartHour <= hour && hour < b.EndHour);
            var possible = courtsCount * daysInMonth;
            var occupancyRate = possible > 0 ? (decimal)occupied / possible : 0m;
            byHour.Add(new { hour, occupancyRate });
        }

        var topCustomers = bookings
            .GroupBy(b => b.CustomerName)
            .Select(g => new
            {
                name = g.Key,
                totalSpent = g.Sum(b => b.TotalAmount),
                bookingsCount = g.Count(),
            })
            .OrderByDescending(c => c.totalSpent)
            .Take(TopCustomersLimit)
            .ToList();

        return Ok(new
        {
            totalRevenue,
            byHour,
            topCustomers,
        });
    }
}
