namespace Padel.Api.Models;

/// <summary>
/// Registro interno de cada cobro parcial o total de una reserva. No se expone
/// tal cual por la API: existe para poder agrupar por medio de pago en
/// GET /api/payments/summary sin perder el detalle cuando una reserva se cobra
/// en varias partes.
/// </summary>
public class BookingPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>"efectivo" | "transferencia" | "tarjeta"</summary>
    public string PaymentMethod { get; set; } = "efectivo";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
