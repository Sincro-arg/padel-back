using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Controllers;

// Caja: cobrar reservas, ver deudas y el resumen del día. Ambos roles cobran;
// el resumen (que muestra montos totales de facturación) es solo para admin.
[ApiController]
[Route("api")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private static readonly string[] ValidPaymentMethods = { "efectivo", "transferencia", "tarjeta" };

    private readonly AppDbContext _db;

    public PaymentsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("payments/debts")]
    public async Task<IActionResult> GetDebts()
    {
        var bookings = await _db.Bookings.ToListAsync();

        var debts = bookings
            .Select(b => new { Booking = b, AmountDue = AmountDue(b) })
            .Where(x => x.AmountDue > 0)
            .OrderBy(x => x.Booking.Date).ThenBy(x => x.Booking.StartHour)
            .Select(x => new
            {
                bookingId = x.Booking.Id,
                customerName = x.Booking.CustomerName,
                date = x.Booking.Date.ToString("yyyy-MM-dd"),
                startHour = x.Booking.StartHour,
                amountDue = x.AmountDue,
            });

        return Ok(debts);
    }

    [HttpGet("payments/summary")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetSummary([FromQuery] string date)
    {
        if (!DateOnly.TryParse(date, out var day))
            return BadRequest(new { error = "Fecha inválida, formato esperado YYYY-MM-DD" });

        var bookingPayments = await _db.BookingPayments
            .Where(p => p.CreatedAt.Year == day.Year && p.CreatedAt.Month == day.Month && p.CreatedAt.Day == day.Day)
            .ToListAsync();

        var productSales = await _db.ProductSales
            .Where(s => s.CreatedAt.Year == day.Year && s.CreatedAt.Month == day.Month && s.CreatedAt.Day == day.Day)
            .ToListAsync();

        decimal SumBy(string method) =>
            bookingPayments.Where(p => p.PaymentMethod == method).Sum(p => p.Amount)
            + productSales.Where(s => s.PaymentMethod == method).Sum(s => s.Amount);

        var cash = SumBy("efectivo");
        var transfer = SumBy("transferencia");
        var card = SumBy("tarjeta");

        var bookings = await _db.Bookings.ToListAsync();
        var debtTotal = bookings.Sum(b => Math.Max(0, AmountDue(b)));

        return Ok(new
        {
            date = day.ToString("yyyy-MM-dd"),
            total = cash + transfer + card,
            cash,
            transfer,
            card,
            debtTotal,
        });
    }

    [HttpPost("bookings/{id:guid}/payments")]
    public async Task<IActionResult> AddPayment(Guid id, [FromBody] BookingPaymentDto dto)
    {
        var booking = await _db.Bookings.Include(b => b.Court).FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound(new { error = "Reserva no encontrada" });

        if (dto.Amount <= 0)
            return BadRequest(new { error = "El monto debe ser mayor a 0" });

        if (!ValidPaymentMethods.Contains(dto.PaymentMethod))
            return BadRequest(new { error = "paymentMethod debe ser 'efectivo', 'transferencia' o 'tarjeta'" });

        _db.BookingPayments.Add(new BookingPayment
        {
            BookingId = booking.Id,
            Amount = dto.Amount,
            PaymentMethod = dto.PaymentMethod,
        });
        await _db.SaveChangesAsync();

        await RecalculatePaymentStatusAsync(booking);
        await _db.SaveChangesAsync();

        return Ok(ToDto(booking));
    }

    /// <summary>
    /// Lista los pagos ya registrados de una reserva, para poder elegir
    /// cuál corregir o borrar desde Caja.
    /// </summary>
    [HttpGet("bookings/{id:guid}/payments")]
    public async Task<IActionResult> GetPayments(Guid id)
    {
        var bookingExists = await _db.Bookings.AnyAsync(b => b.Id == id);
        if (!bookingExists) return NotFound(new { error = "Reserva no encontrada" });

        var payments = await _db.BookingPayments
            .Where(p => p.BookingId == id)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id,
                bookingId = p.BookingId,
                amount = p.Amount,
                paymentMethod = p.PaymentMethod,
                createdAt = p.CreatedAt,
            })
            .ToListAsync();

        return Ok(payments);
    }

    /// <summary>Corrige el monto o el medio de pago de un cobro ya registrado.</summary>
    [HttpPut("bookings/{id:guid}/payments/{paymentId:guid}")]
    public async Task<IActionResult> EditPayment(Guid id, Guid paymentId, [FromBody] BookingPaymentDto dto)
    {
        var booking = await _db.Bookings.Include(b => b.Court).FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound(new { error = "Reserva no encontrada" });

        var payment = await _db.BookingPayments.FirstOrDefaultAsync(p => p.Id == paymentId && p.BookingId == id);
        if (payment == null) return NotFound(new { error = "Pago no encontrado" });

        if (dto.Amount <= 0)
            return BadRequest(new { error = "El monto debe ser mayor a 0" });

        if (!ValidPaymentMethods.Contains(dto.PaymentMethod))
            return BadRequest(new { error = "paymentMethod debe ser 'efectivo', 'transferencia' o 'tarjeta'" });

        payment.Amount = dto.Amount;
        payment.PaymentMethod = dto.PaymentMethod;
        await _db.SaveChangesAsync();

        await RecalculatePaymentStatusAsync(booking);
        await _db.SaveChangesAsync();

        return Ok(ToDto(booking));
    }

    /// <summary>Borra un cobro registrado por error y recalcula lo pagado de la reserva.</summary>
    [HttpDelete("bookings/{id:guid}/payments/{paymentId:guid}")]
    public async Task<IActionResult> DeletePayment(Guid id, Guid paymentId)
    {
        var booking = await _db.Bookings.Include(b => b.Court).FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound(new { error = "Reserva no encontrada" });

        var payment = await _db.BookingPayments.FirstOrDefaultAsync(p => p.Id == paymentId && p.BookingId == id);
        if (payment == null) return NotFound(new { error = "Pago no encontrado" });

        _db.BookingPayments.Remove(payment);
        await _db.SaveChangesAsync();

        await RecalculatePaymentStatusAsync(booking);
        await _db.SaveChangesAsync();

        return Ok(ToDto(booking));
    }

    /// <summary>
    /// Recalcula PaidAmount sumando todos los BookingPayments vigentes de la
    /// reserva (no solo el último movimiento), y deriva PaymentStatus. Así
    /// editar o borrar un pago queda siempre consistente con el total real.
    /// </summary>
    private async Task RecalculatePaymentStatusAsync(Booking booking)
    {
        booking.PaidAmount = await _db.BookingPayments
            .Where(p => p.BookingId == booking.Id)
            .SumAsync(p => p.Amount);

        var totalDue = booking.Status == "cancelled" ? (booking.CancellationFee ?? booking.TotalAmount) : booking.TotalAmount;
        booking.PaymentStatus = booking.PaidAmount <= 0
            ? "pending"
            : booking.PaidAmount >= totalDue ? "paid" : "partial";
    }

    /// <summary>
    /// Lo que todavía debe una reserva: el total (o la penalidad, si está
    /// cancelada) menos lo ya pagado. 0 o negativo significa que está saldada.
    /// </summary>
    private static decimal AmountDue(Booking b)
    {
        var due = b.Status == "cancelled" ? (b.CancellationFee ?? 0m) : b.TotalAmount;
        return due - b.PaidAmount;
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

public class BookingPaymentDto
{
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}
