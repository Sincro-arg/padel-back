using Padel.Api.Models;

namespace Padel.Api.Data;

/// <summary>
/// Seed inicial: canchas, reglas de precio, productos, socios de ejemplo
/// (uno al día, uno con un mes debido y uno bloqueado), un torneo con sus
/// parejas y fixture, y los dos usuarios de ejemplo (admin / empleado) para
/// poder loguearse desde cero.
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!db.Members.Any())
        {
            var alDia = new Member { Name = "Martín Ferreyra", Phone = "11-4444-1111", MembershipFee = 8000m, DiscountPercent = 10m, JoinedAt = today.AddMonths(-2) };
            var unMes = new Member { Name = "Lucía Gómez", Phone = "11-4444-2222", MembershipFee = 8000m, DiscountPercent = 0m, JoinedAt = today.AddMonths(-1) };
            var bloqueado = new Member { Name = "Diego Paz", Phone = "11-4444-3333", MembershipFee = 8000m, DiscountPercent = 5m, JoinedAt = today.AddMonths(-3) };
            db.Members.AddRange(alDia, unMes, bloqueado);

            // Martín pagó los 3 meses desde que se asoció: queda al día (monthsOwed 0).
            for (var i = 0; i <= 2; i++)
            {
                var month = today.AddMonths(-i);
                db.MemberPayments.Add(new MemberPayment
                {
                    MemberId = alDia.Id,
                    Month = month.Month,
                    Year = month.Year,
                    Amount = 8000m,
                    PaymentMethod = "efectivo",
                });
            }

            // Lucía solo pagó el mes anterior: debe el mes en curso (monthsOwed 1, no bloqueada).
            var mesAnterior = today.AddMonths(-1);
            db.MemberPayments.Add(new MemberPayment
            {
                MemberId = unMes.Id,
                Month = mesAnterior.Month,
                Year = mesAnterior.Year,
                Amount = 8000m,
                PaymentMethod = "transferencia",
            });

            // Diego no pagó nunca desde el alta: queda bloqueado (monthsOwed >= 2).
        }

        if (!db.Tournaments.Any())
        {
            var tournament = new Tournament { Name = "Torneo Apertura", Date = today.AddDays(14), RegistrationFee = 5000m };
            db.Tournaments.Add(tournament);

            var pair1 = new Pair { TournamentId = tournament.Id, Player1 = "Martín Ferreyra", Player2 = "Lucía Gómez", Paid = true, PaymentMethod = "efectivo" };
            var pair2 = new Pair { TournamentId = tournament.Id, Player1 = "Diego Paz", Player2 = "Sofía Ríos", Paid = true, PaymentMethod = "transferencia" };
            var pair3 = new Pair { TournamentId = tournament.Id, Player1 = "Nico Suárez", Player2 = "Valen Ortiz", Paid = false };
            var pair4 = new Pair { TournamentId = tournament.Id, Player1 = "Ceci Molina", Player2 = "Pato Ibarra", Paid = true, PaymentMethod = "tarjeta" };
            db.Pairs.AddRange(pair1, pair2, pair3, pair4);

            // Un partido ya jugado, con resultado cargado, y otro pendiente del fixture.
            db.Matches.Add(new Match
            {
                TournamentId = tournament.Id,
                Round = "Cuartos de final",
                Pair1Id = pair1.Id,
                Pair2Id = pair2.Id,
                Score = "6-3 6-4",
                WinnerPairId = pair1.Id,
            });
            db.Matches.Add(new Match
            {
                TournamentId = tournament.Id,
                Round = "Cuartos de final",
                Pair1Id = pair3.Id,
                Pair2Id = pair4.Id,
            });
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

        db.SaveChanges();
    }
}
