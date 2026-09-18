using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Controllers;

// Torneos: organización, cobro de inscripción y carga de resultados. Todo el
// módulo es admin-only, por eso el [Authorize(Roles = "admin")] va a nivel
// de clase y no hace falta repetirlo en cada acción.
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

    // ── Torneos ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tournaments = await _db.Tournaments.OrderBy(t => t.Date).ToListAsync();
        return Ok(tournaments.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TournamentDto dto)
    {
        var (error, date) = Validate(dto);
        if (error != null) return BadRequest(new { error });

        var tournament = new Tournament
        {
            Name = dto.Name!.Trim(),
            Date = date,
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

        var (error, date) = Validate(dto);
        if (error != null) return BadRequest(new { error });

        tournament.Name = dto.Name!.Trim();
        tournament.Date = date;
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

    // ── Parejas ─────────────────────────────────────────────────────────
    [HttpGet("{id:guid}/pairs")]
    public async Task<IActionResult> GetPairs(Guid id)
    {
        var tournamentExists = await _db.Tournaments.AnyAsync(t => t.Id == id);
        if (!tournamentExists) return NotFound(new { error = "Torneo no encontrado" });

        var pairs = await _db.Pairs.Where(p => p.TournamentId == id).ToListAsync();
        return Ok(pairs.Select(ToDto));
    }

    [HttpPost("{id:guid}/pairs")]
    public async Task<IActionResult> CreatePair(Guid id, [FromBody] PairDto dto)
    {
        var tournamentExists = await _db.Tournaments.AnyAsync(t => t.Id == id);
        if (!tournamentExists) return NotFound(new { error = "Torneo no encontrado" });

        var error = ValidatePair(dto);
        if (error != null) return BadRequest(new { error });

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

    // Ruta absoluta /api/tournaments/pairs/{pairId} (no anidada bajo el id del
    // torneo): así lo pide el contrato.
    [HttpDelete("/api/tournaments/pairs/{pairId:guid}")]
    public async Task<IActionResult> DeletePair(Guid pairId)
    {
        var pair = await _db.Pairs.FirstOrDefaultAsync(p => p.Id == pairId);
        if (pair == null) return NotFound(new { error = "Pareja no encontrada" });

        _db.Pairs.Remove(pair);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // ── Partidos / fixture ──────────────────────────────────────────────
    [HttpGet("{id:guid}/matches")]
    public async Task<IActionResult> GetMatches(Guid id)
    {
        var tournamentExists = await _db.Tournaments.AnyAsync(t => t.Id == id);
        if (!tournamentExists) return NotFound(new { error = "Torneo no encontrado" });

        var matches = await _db.Matches.Where(m => m.TournamentId == id).ToListAsync();
        return Ok(matches.Select(ToDto));
    }

    [HttpPost("{id:guid}/matches")]
    public async Task<IActionResult> CreateMatch(Guid id, [FromBody] MatchDto dto)
    {
        var tournamentExists = await _db.Tournaments.AnyAsync(t => t.Id == id);
        if (!tournamentExists) return NotFound(new { error = "Torneo no encontrado" });

        if (dto == null || string.IsNullOrWhiteSpace(dto.Round))
            return BadRequest(new { error = "La ronda es requerida" });
        if (dto.Pair1Id == dto.Pair2Id)
            return BadRequest(new { error = "Las dos parejas del partido tienen que ser distintas" });

        var pair1Exists = await _db.Pairs.AnyAsync(p => p.Id == dto.Pair1Id && p.TournamentId == id);
        if (!pair1Exists) return BadRequest(new { error = "pair1Id no corresponde a una pareja de este torneo" });
        var pair2Exists = await _db.Pairs.AnyAsync(p => p.Id == dto.Pair2Id && p.TournamentId == id);
        if (!pair2Exists) return BadRequest(new { error = "pair2Id no corresponde a una pareja de este torneo" });

        var match = new Match
        {
            TournamentId = id,
            Round = dto.Round.Trim(),
            Pair1Id = dto.Pair1Id,
            Pair2Id = dto.Pair2Id,
        };
        _db.Matches.Add(match);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToDto(match));
    }

    // Ruta absoluta /api/tournaments/matches/{matchId}/result: así lo pide el contrato.
    [HttpPut("/api/tournaments/matches/{matchId:guid}/result")]
    public async Task<IActionResult> UpdateMatchResult(Guid matchId, [FromBody] MatchResultDto dto)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return NotFound(new { error = "Partido no encontrado" });

        if (dto == null || string.IsNullOrWhiteSpace(dto.Score))
            return BadRequest(new { error = "El resultado es requerido" });
        if (dto.WinnerPairId != match.Pair1Id && dto.WinnerPairId != match.Pair2Id)
            return BadRequest(new { error = "winnerPairId debe ser una de las dos parejas del partido" });

        match.Score = dto.Score.Trim();
        match.WinnerPairId = dto.WinnerPairId;
        await _db.SaveChangesAsync();

        return Ok(ToDto(match));
    }

    private static (string? error, DateOnly date) Validate(TournamentDto? dto)
    {
        if (dto == null) return ("Datos inválidos", default);
        if (string.IsNullOrWhiteSpace(dto.Name)) return ("El nombre es requerido", default);
        if (!DateOnly.TryParse(dto.Date, out var date)) return ("Fecha inválida, formato esperado YYYY-MM-DD", default);
        if (dto.RegistrationFee < 0) return ("registrationFee no puede ser negativo", default);
        return (null, date);
    }

    private static string? ValidatePair(PairDto? dto)
    {
        if (dto == null) return "Datos inválidos";
        if (string.IsNullOrWhiteSpace(dto.Player1)) return "player1 es requerido";
        if (string.IsNullOrWhiteSpace(dto.Player2)) return "player2 es requerido";
        if (dto.Paid && !ValidPaymentMethods.Contains(dto.PaymentMethod))
            return "paymentMethod debe ser 'efectivo', 'transferencia' o 'tarjeta'";
        return null;
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
