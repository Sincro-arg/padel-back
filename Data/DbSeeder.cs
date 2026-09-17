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

        db.SaveChanges();
    }
}
