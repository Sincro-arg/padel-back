using Padel.Api.Models;

namespace Padel.Api.Data;

/// <summary>
/// Seed inicial: canchas, reglas de precio y los dos usuarios de ejemplo
/// (admin / empleado) para poder loguearse desde cero.
/// </summary>
public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.Courts.Any())
        {
            db.Courts.AddRange(
                new Court { Name = "Cancha 1" },
                new Court { Name = "Cancha 2" },
                new Court { Name = "Cancha 3" }
            );
        }

        // Se persisten acá para poder usar sus Id reales al armar las reservas
        // de ejemplo de más abajo.
        db.SaveChanges();

        if (!db.PriceRules.Any())
        {
            db.PriceRules.AddRange(
                new PriceRule { DayType = "weekday", StartHour = 8, EndHour = 17, PricePerHour = 4000m },
                new PriceRule { DayType = "weekday", StartHour = 17, EndHour = 24, PricePerHour = 6000m },
                new PriceRule { DayType = "weekend", StartHour = 8, EndHour = 24, PricePerHour = 7000m }
            );
        }

        if (!db.Products.Any())
        {
            db.Products.AddRange(
                new Product { Name = "Paleta", Type = "alquiler", Stock = 10, MinStock = 2, Price = 1500m },
                new Product { Name = "Pelotas de pádel (tubo x3)", Type = "venta", Stock = 20, MinStock = 5, Price = 3500m },
                new Product { Name = "Agua 500ml", Type = "venta", Stock = 3, MinStock = 5, Price = 1200m },
                new Product { Name = "Gaseosa 500ml", Type = "venta", Stock = 15, MinStock = 5, Price = 1500m }
            );
        }

        if (!db.Users.Any())
        {
            db.Users.AddRange(
                new User
                {
                    Username = "admin",
                    Name = "Administrador",
                    Role = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                },
                new User
                {
                    Username = "empleado",
                    Name = "Empleado",
                    Role = "empleado",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Empleado123!"),
                }
            );
        }

        // Reservas de hoy, para que el dashboard ("Turnos de hoy" / "Canchas
        // ahora") no arranque vacío la primera vez que se levanta el back.
        // Se siembran una sola vez por día: si ya hay alguna reserva con la
        // fecha de hoy (cargada a mano o por una corrida anterior en el
        // mismo día), no se agregan de nuevo.
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!db.Bookings.Any(b => b.Date == today))
        {
            var courts = db.Courts.OrderBy(c => c.Name).ToList();
            if (courts.Count >= 1)
            {
                var court1 = courts[0];
                var court2 = courts.Count >= 2 ? courts[1] : courts[0];
                var court3 = courts.Count >= 3 ? courts[2] : courts[0];

                // Ancla del "turno en curso" a la hora real, para que al abrir
                // el dashboard se vea al menos una cancha "occupied" ahora.
                var nowHour = Math.Clamp(DateTime.Now.Hour, 8, 22);

                db.Bookings.AddRange(
                    new Booking
                    {
                        CourtId = court1.Id,
                        CustomerName = "Martín Gómez",
                        CustomerPhone = "1122334455",
                        Date = today,
                        StartHour = nowHour,
                        EndHour = nowHour + 1,
                        Status = "confirmed",
                        TotalAmount = 6000m,
                        PaidAmount = 6000m,
                        PaymentStatus = "paid",
                    },
                    new Booking
                    {
                        CourtId = court2.Id,
                        CustomerName = "Lucía Fernández",
                        CustomerPhone = "1133445566",
                        Date = today,
                        StartHour = Math.Min(nowHour + 2, 22),
                        EndHour = Math.Min(nowHour + 3, 23),
                        Status = "confirmed",
                        TotalAmount = 6000m,
                        PaidAmount = 0m,
                        PaymentStatus = "pending",
                    },
                    new Booking
                    {
                        CourtId = court3.Id,
                        CustomerName = "Diego Ramírez",
                        CustomerPhone = "1144556677",
                        Date = today,
                        StartHour = Math.Max(nowHour - 2, 8),
                        EndHour = Math.Max(nowHour - 1, 9),
                        Status = "confirmed",
                        TotalAmount = 4000m,
                        PaidAmount = 2000m,
                        PaymentStatus = "partial",
                    }
                );
            }
        }

        // Socios de ejemplo, con estados variados de cuota (al día, atrasado
        // 1 mes, atrasado 2+/bloqueado) para que la pantalla Socios no abra
        // vacía y se pueda probar la regla de bloqueo de reservas.
        if (!db.Members.Any())
        {
            // Antigüedad fija de 6 meses para tener historial de cuotas.
            var joinedAt = today.AddMonths(-6);

            var members = new List<Member>
            {
                new Member { Name = "Sofía Torres", Phone = "1155667788", MembershipFee = 10000m, DiscountPercent = 0m, JoinedAt = joinedAt },
                new Member { Name = "Facundo López", Phone = "1166778899", MembershipFee = 10000m, DiscountPercent = 10m, JoinedAt = joinedAt },
                new Member { Name = "Camila Ruiz", Phone = "1177889900", MembershipFee = 12000m, DiscountPercent = 0m, JoinedAt = joinedAt },
                new Member { Name = "Nicolás Sosa", Phone = "1188990011", MembershipFee = 10000m, DiscountPercent = 0m, JoinedAt = joinedAt },
                new Member { Name = "Valentina Díaz", Phone = "1199001122", MembershipFee = 10000m, DiscountPercent = 5m, JoinedAt = joinedAt },
                new Member { Name = "Tomás Ibáñez", Phone = "1100112233", MembershipFee = 10000m, DiscountPercent = 0m, JoinedAt = joinedAt },
                new Member { Name = "Julieta Vega", Phone = "1111223344", MembershipFee = 12000m, DiscountPercent = 0m, JoinedAt = joinedAt },
                new Member { Name = "Bruno Acosta", Phone = "1122334400", MembershipFee = 10000m, DiscountPercent = 0m, JoinedAt = joinedAt },
            };
            db.Members.AddRange(members);
            db.SaveChanges();

            // Meses desde el alta hasta el actual (ambos inclusive), del más
            // viejo al más nuevo, igual que el cálculo de MemberStatus.MonthsOwed.
            var months = new List<(int Year, int Month)>();
            var cursor = new DateOnly(joinedAt.Year, joinedAt.Month, 1);
            var limit = new DateOnly(today.Year, today.Month, 1);
            while (cursor <= limit)
            {
                months.Add((cursor.Year, cursor.Month));
                cursor = cursor.AddMonths(1);
            }

            var payments = new List<MemberPayment>();
            void PayMonths(Member member, int countToPay)
            {
                for (var i = 0; i < countToPay; i++)
                {
                    var (year, month) = months[i];
                    payments.Add(new MemberPayment
                    {
                        MemberId = member.Id,
                        Year = year,
                        Month = month,
                        Amount = member.MembershipFee * (1 - member.DiscountPercent / 100m),
                        PaymentMethod = i % 2 == 0 ? "efectivo" : "transferencia",
                    });
                }
            }

            // Al día: pagaron todos los meses, incluido el actual.
            PayMonths(members[0], months.Count);
            PayMonths(members[1], months.Count);
            PayMonths(members[2], months.Count);

            // Atrasados 1 mes: les falta el mes actual.
            PayMonths(members[3], months.Count - 1);
            PayMonths(members[4], months.Count - 1);

            // Atrasados 2+ meses (bloqueados): les faltan los últimos 2 o 3 meses.
            PayMonths(members[5], months.Count - 2);
            PayMonths(members[6], months.Count - 2);
            PayMonths(members[7], months.Count - 3);

            db.MemberPayments.AddRange(payments);
        }

        if (!db.Tournaments.Any())
        {
            var tournament = new Tournament
            {
                Name = "Torneo Apertura",
                Date = today.AddDays(14),
                RegistrationFee = 8000m,
            };
            db.Tournaments.Add(tournament);
            db.SaveChanges();

            var pair1 = new Pair
            {
                TournamentId = tournament.Id,
                Player1 = "Martín Gómez",
                Player2 = "Lucía Fernández",
                Paid = true,
                PaymentMethod = "efectivo",
            };
            var pair2 = new Pair
            {
                TournamentId = tournament.Id,
                Player1 = "Diego Ramírez",
                Player2 = "Sofía Torres",
                Paid = true,
                PaymentMethod = "transferencia",
            };
            var pair3 = new Pair
            {
                TournamentId = tournament.Id,
                Player1 = "Facundo López",
                Player2 = "Camila Ruiz",
                Paid = false,
            };
            var pair4 = new Pair
            {
                TournamentId = tournament.Id,
                Player1 = "Nicolás Sosa",
                Player2 = "Valentina Díaz",
                Paid = true,
                PaymentMethod = "tarjeta",
            };
            db.Pairs.AddRange(pair1, pair2, pair3, pair4);
            db.SaveChanges();

            db.Matches.Add(new Match
            {
                TournamentId = tournament.Id,
                Round = "Cuartos de final",
                Pair1Id = pair1.Id,
                Pair2Id = pair2.Id,
                Score = "6-3 6-4",
                WinnerPairId = pair1.Id,
            });
        }

        db.SaveChanges();
    }
}
