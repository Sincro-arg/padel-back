using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

// Socios: alta, edición y baja las hace solo el admin. El empleado puede ver
// el listado (para saber si están al día antes de reservarles cancha) y
// cobrarles la cuota mensual, igual que cobra reservas en PaymentsController.
[ApiController]
[Route("api/members")]
[Authorize]
public class MembersController : ControllerBase
{
    private static readonly string[] ValidPaymentMethods = { "efectivo", "transferencia", "tarjeta" };

    private readonly AppDbContext _db;

    public MembersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var members = await _db.Members.OrderBy(m => m.Name).ToListAsync();
        var payments = await _db.MemberPayments.ToListAsync();

        return Ok(members.Select(m => ToDto(m, payments.Where(p => p.MemberId == m.Id))));
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] MemberDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { error });

        var member = new Member
        {
            Name = dto.Name!.Trim(),
            Phone = dto.Phone?.Trim() ?? string.Empty,
            MembershipFee = dto.MembershipFee,
            DiscountPercent = dto.DiscountPercent,
        };
        _db.Members.Add(member);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToDto(member, Enumerable.Empty<MemberPayment>()));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] MemberDto dto)
    {
        var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == id);
        if (member == null) return NotFound(new { error = "Socio no encontrado" });

        var error = Validate(dto);
        if (error != null) return BadRequest(new { error });

        member.Name = dto.Name!.Trim();
        member.Phone = dto.Phone?.Trim() ?? string.Empty;
        member.MembershipFee = dto.MembershipFee;
        member.DiscountPercent = dto.DiscountPercent;
        await _db.SaveChangesAsync();

        var payments = await _db.MemberPayments.Where(p => p.MemberId == member.Id).ToListAsync();
        return Ok(ToDto(member, payments));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == id);
        if (member == null) return NotFound(new { error = "Socio no encontrado" });

        _db.Members.Remove(member);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:guid}/payments")]
    public async Task<IActionResult> GetPayments(Guid id)
    {
        var exists = await _db.Members.AnyAsync(m => m.Id == id);
        if (!exists) return NotFound(new { error = "Socio no encontrado" });

        var payments = await _db.MemberPayments
            .Where(p => p.MemberId == id)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new
            {
                id = p.Id,
                memberId = p.MemberId,
                month = p.Month,
                year = p.Year,
                amount = p.Amount,
                paymentMethod = p.PaymentMethod,
                paidAt = p.PaidAt,
            })
            .ToListAsync();

        return Ok(payments);
    }

    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> AddPayment(Guid id, [FromBody] MemberPaymentDto dto)
    {
        var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == id);
        if (member == null) return NotFound(new { error = "Socio no encontrado" });

        if (dto.Month < 1 || dto.Month > 12)
            return BadRequest(new { error = "El mes debe estar entre 1 y 12" });

        if (dto.Amount <= 0)
            return BadRequest(new { error = "El monto debe ser mayor a 0" });

        if (!ValidPaymentMethods.Contains(dto.PaymentMethod))
            return BadRequest(new { error = "paymentMethod debe ser 'efectivo', 'transferencia' o 'tarjeta'" });

        var payment = new MemberPayment
        {
            MemberId = member.Id,
            Month = dto.Month,
            Year = dto.Year,
            Amount = dto.Amount,
            PaymentMethod = dto.PaymentMethod,
        };
        _db.MemberPayments.Add(payment);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, new
        {
            id = payment.Id,
            memberId = payment.MemberId,
            month = payment.Month,
            year = payment.Year,
            amount = payment.Amount,
            paymentMethod = payment.PaymentMethod,
            paidAt = payment.PaidAt,
        });
    }

    private static object ToDto(Member m, IEnumerable<MemberPayment> payments)
    {
        var paymentsList = payments as ICollection<MemberPayment> ?? payments.ToList();
        var monthsOwed = MemberStatus.MonthsOwed(m.JoinedAt, paymentsList.Select(p => (p.Year, p.Month)));
        return new
        {
            id = m.Id,
            name = m.Name,
            phone = m.Phone,
            membershipFee = m.MembershipFee,
            discountPercent = m.DiscountPercent,
            monthsOwed,
            isBlocked = monthsOwed >= 2,
        };
    }

    private static string? Validate(MemberDto? dto)
    {
        if (dto == null) return "Datos inválidos";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "El nombre es requerido";
        if (dto.MembershipFee < 0) return "La cuota no puede ser negativa";
        if (dto.DiscountPercent < 0 || dto.DiscountPercent > 100) return "El descuento debe estar entre 0 y 100";
        return null;
    }
}

public class MemberDto
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public decimal MembershipFee { get; set; }
    public decimal DiscountPercent { get; set; }
}

public class MemberPaymentDto
{
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}
