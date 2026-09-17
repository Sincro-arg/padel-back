namespace Padel.Api.Models;

/// <summary>
/// Turno fijo semanal (ej. todos los martes 20hs). Al crearse genera
/// automáticamente los Bookings concretos de las próximas 8 semanas,
/// vinculados por RecurringBookingId.
/// </summary>
public class RecurringBooking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourtId { get; set; }
    public Court? Court { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;

    /// <summary>0=domingo … 6=sábado</summary>
    public int Weekday { get; set; }

    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public Guid? MemberId { get; set; }
}
