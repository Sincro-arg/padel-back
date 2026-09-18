using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Controllers;

// Torneos: organiza, cobra inscripción y carga resultados solo el admin.
// Las rutas de parejas y partidos viven acá adentro aunque algunas cuelguen
// de /api/tournaments/pairs y /api/tournaments/matches en vez de anidar bajo
// el id del torneo, tal como lo pide el contrato.
[ApiController]
[Route("api/tournaments")]
[Authorize(Roles = "admin")]
public class TournamentsController : ControllerBase
{
    private static readonly string[] ValidPaymentMethods = { "efectivo", "transferencia", "tarjeta" };

    private readonly AppDbContext _db;

    public TournamentsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tournaments = await _db.Tournaments.OrderBy(t => t.Date).ToListAsync();
        return Ok(tournaments.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TournamentDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { error });

        var tournament = new Tournament
        {
            Name = dto.Name!.Trim(),
            Date = DateOnly.Parse(dto.Date!),
            RegistrationFee = dto.RegistrationFee,
        };
        _db.Tournaments.Add(tournament);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToDto(tournament));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TournamentDto dto)
    {
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.Id == id);
        if (tournament == null) return NotFound(new { error = "Torneo no encontrado" });

        var error = Validate(dto);
        if (error != null) return BadRequest(new { error });

        tournament.Name = dto.Name!.Trim();
        tournament.Date = DateOnly.Parse(dto.Date!);
        tournament.RegistrationFee = dto.RegistrationFee;
        await _db.SaveChangesAsync();

        return Ok(ToDto(tournament));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.Id == id);
        if (tournament == null) return NotFound(new { error = "Torneo no encontrado" });

        _db.Tournaments.Remove(tournament);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:guid}/pairs")]
    public async Task<IActionResult> GetPairs(Guid id)
    {
        var exists = await _db.Tournaments.AnyAsync(t => t.Id == id);
        if (!exists) return NotFound(new { error = "Torneo no encontrado" });

        var pairs = await _db.Pairs.Where(p => p.TournamentId == id).ToListAsync();
        return Ok(pairs.Select(ToDto));
    }

    [HttpPost("{id:guid}/pairs")]
    public async Task<IActionResult> CreatePair(Guid id, [FromBody] PairDto dto)
    {
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.Id == id);
        if (tournament == null) return NotFound(new { error = "Torneo no encontrado" });

        if (string.IsNullOrWhiteSpace(dto.Player1) || string.IsNullOrWhiteSpace(dto.Player2))
            return BadRequest(new { error = "Los dos jugadores son requeridos" });

        if (dto.Paid && (dto.PaymentMethod == null || !ValidPaymentMethods.Contains(dto.PaymentMethod)))
            return BadRequest(new { error = "paymentMethod debe ser 'efectivo', 'transferencia' o 'tarjeta'" });

        var pair = new Pair
        {
            TournamentId = id,
            Player1 = dto.Player1!.Trim(),
            Player2 = dto.Player2!.Trim(),
            Paid = dto.Paid,
            PaymentMethod = dto.Paid ? dto.PaymentMethod : null,
        };
        _db.Pairs.Add(pair);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToDto(pair));
    }

    [HttpDelete("pairs/{pairId:guid}")]
    public async Task<IActionResult> DeletePair(Guid pairId)
    {
        var pair = await _db.Pairs.FirstOrDefaultAsync(p => p.Id == pairId);
        if (pair == null) return NotFound(new { error = "Pareja no encontrada" });

        _db.Pairs.Remove(pair);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:guid}/matches")]
    public async Task<IActionResult> GetMatches(Guid id)
    {
        var exists = await _db.Tournaments.AnyAsync(t => t.Id == id);
        if (!exists) return NotFound(new { error = "Torneo no encontrado" });

        var matches = await _db.Matches.Where(m => m.TournamentId == id).ToListAsync();
        return Ok(matches.Select(ToDto));
    }

    [HttpPost("{id:guid}/matches")]
    public async Task<IActionResult> CreateMatch(Guid id, [FromBody] MatchDto dto)
    {
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(t => t.Id == id);
        if (tournament == null) return NotFound(new { error = "Torneo no encontrado" });

        if (string.IsNullOrWhiteSpace(dto.Round))
            return BadRequest(new { error = "La ronda es requerida" });

        if (dto.Pair1Id == dto.Pair2Id)
            return BadRequest(new { error = "Las dos parejas del partido tienen que ser distintas" });

        var pair1Exists = await _db.Pairs.AnyAsync(p => p.Id == dto.Pair1Id && p.TournamentId == id);
        var pair2Exists = await _db.Pairs.AnyAsync(p => p.Id == dto.Pair2Id && p.TournamentId == id);
        if (!pair1Exists || !pair2Exists)
            return BadRequest(new { error = "Las parejas indicadas no pertenecen a este torneo" });

        var match = new Match
        {
            TournamentId = id,
            Round = dto.Round!.Trim(),
            Pair1Id = dto.Pair1Id,
            Pair2Id = dto.Pair2Id,
        };
        _db.Matches.Add(match);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToDto(match));
    }

    [HttpPut("matches/{matchId:guid}/result")]
    public async Task<IActionResult> UpdateResult(Guid matchId, [FromBody] MatchResultDto dto)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return NotFound(new { error = "Partido no encontrado" });

        if (string.IsNullOrWhiteSpace(dto.Score))
            return BadRequest(new { error = "El resultado es requerido" });

        if (dto.WinnerPairId != match.Pair1Id && dto.WinnerPairId != match.Pair2Id)
            return BadRequest(new { error = "El ganador debe ser una de las dos parejas del partido" });

        match.Score = dto.Score!.Trim();
        match.WinnerPairId = dto.WinnerPairId;
        await _db.SaveChangesAsync();

        return Ok(ToDto(match));
    }

    private static object ToDto(Tournament t) => new
    {
        id = t.Id,
        name = t.Name,
        date = t.Date.ToString("yyyy-MM-dd"),
        registrationFee = t.RegistrationFee,
    };

    private static object ToDto(Pair p) => new
    {
        id = p.Id,
        tournamentId = p.TournamentId,
        player1 = p.Player1,
        player2 = p.Player2,
        paid = p.Paid,
        paymentMethod = p.PaymentMethod,
    };

    private static object ToDto(Match m) => new
    {
        id = m.Id,
        tournamentId = m.TournamentId,
        round = m.Round,
        pair1Id = m.Pair1Id,
        pair2Id = m.Pair2Id,
        score = m.Score,
        winnerPairId = m.WinnerPairId,
    };

    private static string? Validate(TournamentDto? dto)
    {
        if (dto == null) return "Datos inválidos";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "El nombre es requerido";
        if (!DateOnly.TryParse(dto.Date, out _)) return "La fecha es inválida";
        if (dto.RegistrationFee < 0) return "La inscripción no puede ser negativa";
        return null;
    }
}

public class TournamentDto
{
    public string? Name { get; set; }
    public string? Date { get; set; }
    public decimal RegistrationFee { get; set; }
}

public class PairDto
{
    public string? Player1 { get; set; }
    public string? Player2 { get; set; }
    public bool Paid { get; set; }
    public string? PaymentMethod { get; set; }
}

public class MatchDto
{
    public string? Round { get; set; }
    public Guid Pair1Id { get; set; }
    public Guid Pair2Id { get; set; }
}

public class MatchResultDto
{
    public string? Score { get; set; }
    public Guid WinnerPairId { get; set; }
}
