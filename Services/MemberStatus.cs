using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Services;

/// <summary>
/// Cálculo de meses adeudados de un socio. Lo usan MembersController (para
/// mostrar monthsOwed/isBlocked en el listado) y BookingsController (para
/// rechazar con 403 una reserva de un socio bloqueado).
/// </summary>
public static class MemberStatus
{
    /// <summary>
    /// Desde el mes de alta del socio hasta el mes actual (ambos inclusive),
    /// cuenta cuántos de esos meses no tienen un pago registrado. Un socio
    /// recién dado de alta ya debe el mes en curso hasta que se le cobre esa
    /// primera cuota.
    /// </summary>
    public static int MonthsOwed(DateOnly joinedAt, IEnumerable<(int Year, int Month)> paidPeriods)
    {
        var paid = paidPeriods.ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var cursor = new DateOnly(joinedAt.Year, joinedAt.Month, 1);
        var limit = new DateOnly(today.Year, today.Month, 1);

        var owed = 0;
        while (cursor <= limit)
        {
            if (!paid.Contains((cursor.Year, cursor.Month))) owed++;
            cursor = cursor.AddMonths(1);
        }
        return owed;
    }

    public static bool IsBlocked(Member member, IEnumerable<MemberPayment> payments) =>
        MonthsOwed(member.JoinedAt, payments.Select(p => (p.Year, p.Month))) >= 2;

    public static async Task<bool> IsBlockedAsync(AppDbContext db, Guid memberId)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null) return false;

        var payments = await db.MemberPayments.Where(p => p.MemberId == memberId).ToListAsync();
        return IsBlocked(member, payments);
    }
}
