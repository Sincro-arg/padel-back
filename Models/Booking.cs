namespace Padel.Api.Models;

/// <summary>
/// Reserva concreta de una cancha en una fecha y horario. El modelo completo
/// (creación, edición, solapamiento, cancelación) lo arma la tarea de Bookings;
/// acá solo se define lo necesario para que Payments/Stock puedan cobrar y
/// vender sobre una reserva existente.
/// </summary>
public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourtId { get; set; }
    public Court? Court { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public Guid? MemberId { get; set; }

    /// <summary>"confirmed" | "cancelled"</summary>
    public string Status { get; set; } = "confirmed";

    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }

    /// <summary>"pending" | "partial" | "paid"</summary>
    public string PaymentStatus { get; set; } = "pending";

    public decimal? CancellationFee { get; set; }
    public bool IsRecurring { get; set; }
    public Guid? RecurringBookingId { get; set; }
}
