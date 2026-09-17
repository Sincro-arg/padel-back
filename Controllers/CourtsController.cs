using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Controllers;

[ApiController]
[Route("api/courts")]
[Authorize]
public class CourtsController : ControllerBase
{
    private readonly AppDbContext _db;

    public CourtsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var courts = await _db.Courts
            .OrderBy(c => c.Name)
            .Select(c => new { id = c.Id, name = c.Name })
            .ToListAsync();
        return Ok(courts);
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] CourtDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
            return BadRequest(new { error = "El nombre es requerido" });

        var court = new Court { Name = dto.Name.Trim() };
        _db.Courts.Add(court);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, new { id = court.Id, name = court.Name });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CourtDto dto)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == id);
        if (court == null) return NotFound(new { error = "Cancha no encontrada" });

        if (string.IsNullOrWhiteSpace(dto?.Name))
            return BadRequest(new { error = "El nombre es requerido" });

        court.Name = dto.Name.Trim();
        await _db.SaveChangesAsync();

        return Ok(new { id = court.Id, name = court.Name });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == id);
        if (court == null) return NotFound(new { error = "Cancha no encontrada" });

        _db.Courts.Remove(court);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}

public class CourtDto
{
    public string Name { get; set; } = string.Empty;
}
