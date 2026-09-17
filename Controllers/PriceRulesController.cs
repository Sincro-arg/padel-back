using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Controllers;

// Precios: solo el admin ve o toca esto (el empleado no ve precios ni reportes).
[ApiController]
[Route("api/price-rules")]
[Authorize(Roles = "admin")]
public class PriceRulesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PriceRulesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rules = await _db.PriceRules
            .OrderBy(r => r.DayType).ThenBy(r => r.StartHour)
            .Select(r => new
            {
                id = r.Id,
                dayType = r.DayType,
                startHour = r.StartHour,
                endHour = r.EndHour,
                pricePerHour = r.PricePerHour,
            })
            .ToListAsync();
        return Ok(rules);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PriceRuleDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { error });

        var rule = new PriceRule
        {
            DayType = dto.DayType!,
            StartHour = dto.StartHour,
            EndHour = dto.EndHour,
            PricePerHour = dto.PricePerHour,
        };
        _db.PriceRules.Add(rule);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToDto(rule));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] PriceRuleDto dto)
    {
        var rule = await _db.PriceRules.FirstOrDefaultAsync(r => r.Id == id);
        if (rule == null) return NotFound(new { error = "Regla de precio no encontrada" });

        var error = Validate(dto);
        if (error != null) return BadRequest(new { error });

        rule.DayType = dto.DayType!;
        rule.StartHour = dto.StartHour;
        rule.EndHour = dto.EndHour;
        rule.PricePerHour = dto.PricePerHour;
        await _db.SaveChangesAsync();

        return Ok(ToDto(rule));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var rule = await _db.PriceRules.FirstOrDefaultAsync(r => r.Id == id);
        if (rule == null) return NotFound(new { error = "Regla de precio no encontrada" });

        _db.PriceRules.Remove(rule);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static object ToDto(PriceRule r) => new
    {
        id = r.Id,
        dayType = r.DayType,
        startHour = r.StartHour,
        endHour = r.EndHour,
        pricePerHour = r.PricePerHour,
    };

    private static string? Validate(PriceRuleDto? dto)
    {
        if (dto == null) return "Datos inválidos";
        if (dto.DayType != "weekday" && dto.DayType != "weekend")
            return "dayType debe ser 'weekday' o 'weekend'";
        if (dto.StartHour < 8 || dto.StartHour > 24 || dto.EndHour < 8 || dto.EndHour > 24)
            return "Las horas deben estar entre 8 y 24";
        if (dto.StartHour >= dto.EndHour)
            return "startHour debe ser menor a endHour";
        if (dto.PricePerHour <= 0)
            return "pricePerHour debe ser mayor a 0";
        return null;
    }
}

public class PriceRuleDto
{
    public string? DayType { get; set; }
    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public decimal PricePerHour { get; set; }
}
