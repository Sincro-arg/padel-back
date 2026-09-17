using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Services;

/// <summary>
/// Cálculo de meses adeudados por un socio. Lo usan MembersController (para
/// exponer monthsOwed/isBlocked) y BookingsController (para bloquear el alta
/// de una reserva a nombre de un socio con 2 o más cuotas adeudadas).
/// </summary>
public static class MemberDebt
{
    /// <summary>
    /// Cuenta, desde el mes de alta del socio hasta el mes actual (ambos
    /// inclusive), cuántos de esos meses no tienen un pago registrado en
    /// MemberPayments. Un socio recién dado de alta ya debe el mes en curso
    /// hasta que se le cobre esa primera cuota.
    /// </summary>
    public static int MonthsOwed(Member member, IEnumerable<MemberPayment> payments)
    {
        var paid = payments.Select(p => (p.Year, p.Month)).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var cursor = new DateOnly(member.JoinedAt.Year, member.JoinedAt.Month, 1);
        var limit = new DateOnly(today.Year, today.Month, 1);

        var owed = 0;
        while (cursor <= limit)
        {
            if (!paid.Contains((cursor.Year, cursor.Month))) owed++;
            cursor = cursor.AddMonths(1);
        }
        return owed;
    }

    public static async Task<int> MonthsOwedAsync(AppDbContext db, Member member)
    {
        var payments = await db.MemberPayments.Where(p => p.MemberId == member.Id).ToListAsync();
        return MonthsOwed(member, payments);
    }
}
