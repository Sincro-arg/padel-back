using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;

namespace Padel.Api.Tests;

/// <summary>
/// Levanta la app entera contra una base InMemory propia por instancia (no toca
/// SQLite ni el disco). Usa los valores de Jwt de appsettings.json tal cual —no los
/// pisa acá— porque Program.cs lee builder.Configuration["Jwt:..."] ANTES de
/// builder.Build(), momento en el que WebApplicationFactory todavía no aplicó un
/// eventual ConfigureAppConfiguration de este factory. Si se pisara acá, el token se
/// firmaría con esos valores (leídos por IConfiguration ya inyectado, después del
/// build) pero se validaría con los de appsettings.json (leídos antes), y todo
/// request autenticado terminaría en 401.
/// </summary>
public class PadelApiFactory : WebApplicationFactory<Program>
{
    // Fijo por instancia: AddDbContext registra las options como Scoped, así que
    // el lambda de configuración se vuelve a ejecutar en cada scope. Si el nombre
    // saliera de un Guid.NewGuid() ahí adentro, cada request terminaría con una
    // base InMemory distinta y vacía (la seedeada quedaba en otra).
    private readonly string _dbName = $"padel-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // AddDbContext<AppDbContext> ya corrió (con Sqlite) al construir el host de
            // Program.cs. Las llamadas a AddDbContext se COMPONEN, no se reemplazan: si
            // solo saco el DbContextOptions<AppDbContext>, la config de Sqlite sigue
            // registrada vía IDbContextOptionsConfiguration<AppDbContext> y termina
            // mezclada con la de InMemory. Hay que sacar todo lo que mencione AppDbContext.
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext)))
            ).ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }
}
