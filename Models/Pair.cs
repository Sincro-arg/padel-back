namespace Padel.Api.Models;

/// <summary>Pareja inscripta en un torneo, con el pago de la inscripción.</summary>
public class Pair
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TournamentId { get; set; }
    public string Player1 { get; set; } = string.Empty;
    public string Player2 { get; set; } = string.Empty;
    public bool Paid { get; set; }

    /// <summary>"efectivo" | "transferencia" | "tarjeta"; null si Paid es false.</summary>
    public string? PaymentMethod { get; set; }
}
