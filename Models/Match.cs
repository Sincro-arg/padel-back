namespace Padel.Api.Models;

/// <summary>Partido del fixture de un torneo, entre dos parejas ya inscriptas.</summary>
public class Match
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TournamentId { get; set; }
    public string Round { get; set; } = string.Empty;
    public Guid Pair1Id { get; set; }
    public Guid Pair2Id { get; set; }
    public string? Score { get; set; }
    public Guid? WinnerPairId { get; set; }
}
