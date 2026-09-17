namespace Padel.Api.Models;

/// <summary>
/// Socio del club. JoinedAt se usa solo internamente para calcular
/// MonthsOwed en MembersController; no se expone tal cual por la API.
/// </summary>
public class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public decimal MembershipFee { get; set; }
    public decimal DiscountPercent { get; set; }
    public DateOnly JoinedAt { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
