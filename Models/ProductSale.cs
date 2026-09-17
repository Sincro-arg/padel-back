namespace Padel.Api.Models;

public class ProductSale
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public Guid? BookingId { get; set; }

    /// <summary>"efectivo" | "transferencia" | "tarjeta"</summary>
    public string PaymentMethod { get; set; } = "efectivo";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
