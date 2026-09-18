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

        db.SaveChanges();
    }
}
