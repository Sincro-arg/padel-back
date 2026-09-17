namespace Padel.Api.Models;

/// <summary>Pago de la cuota mensual de un socio.</summary>
public class MemberPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MemberId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal Amount { get; set; }

    /// <summary>"efectivo" | "transferencia" | "tarjeta"</summary>
    public string PaymentMethod { get; set; } = "efectivo";

    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}
