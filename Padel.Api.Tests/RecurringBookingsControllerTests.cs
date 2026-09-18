using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Tests;

public class RecurringBookingsControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public RecurringBookingsControllerTests(PadelApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        loginResponse.EnsureSuccessStatusCode();

        var json = await loginResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var token = doc.RootElement.GetProperty("token").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Court SeedCourt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var court = new Court { Name = $"Cancha de test {Guid.NewGuid()}" };
        db.Courts.Add(court);
        db.SaveChanges();
        return court;
    }

    [Fact]
    public async Task Create_GeneraOchoReservasSemanales()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var weekday = (int)DateTime.Now.DayOfWeek;

        var response = await client.PostAsJsonAsync("/api/recurring-bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente fijo",
            customerPhone = "1122334455",
            weekday,
            startHour = 20,
            endHour = 21,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var recurringId = body.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generated = await db.Bookings.Where(b => b.RecurringBookingId == recurringId).ToListAsync();

        Assert.Equal(8, generated.Count);
        Assert.All(generated, b => Assert.True(b.IsRecurring));
        Assert.All(generated, b => Assert.Equal("confirmed", b.Status));
        // Las 8 fechas tienen que ser el mismo día de semana pedido, separadas por 7 días.
        Assert.All(generated, b => Assert.Equal((DayOfWeek)weekday, b.Date.DayOfWeek));
        var ordered = generated.OrderBy(b => b.Date).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Assert.Equal(7, ordered[i].Date.DayNumber - ordered[i - 1].Date.DayNumber);
    }

    [Fact]
    public async Task Create_NoPisaUnaReservaQueYaExisteEsaSemana()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var weekday = (int)DateTime.Now.DayOfWeek;

        var firstOccurrence = DateOnly.FromDateTime(DateTime.Now);
        while ((int)firstOccurrence.DayOfWeek != weekday) firstOccurrence = firstOccurrence.AddDays(1);

        await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Ya estaba reservado",
            customerPhone = "9999999999",
            date = firstOccurrence.ToString("yyyy-MM-dd"),
            startHour = 20,
            endHour = 21,
        });

        var response = await client.PostAsJsonAsync("/api/recurring-bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente fijo",
            customerPhone = "1122334455",
            weekday,
            startHour = 20,
            endHour = 21,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var recurringId = body.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generated = await db.Bookings.Where(b => b.RecurringBookingId == recurringId).ToListAsync();

        // Se saltea la semana que ya tenía algo cargado: quedan 7, no 8.
        Assert.Equal(7, generated.Count);
        Assert.DoesNotContain(generated, b => b.Date == firstOccurrence);
    }

    [Fact]
    public async Task GetAll_DevuelveLosTurnosFijosCreados()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var weekday = (int)DateTime.Now.DayOfWeek;

        var createResponse = await client.PostAsJsonAsync("/api/recurring-bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente fijo para listar",
            customerPhone = "1122334455",
            weekday,
            startHour = 22,
            endHour = 23,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var recurringId = created.GetProperty("id").GetGuid();

        var response = await client.GetAsync("/api/recurring-bookings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        var match = list.EnumerateArray().First(r => r.GetProperty("id").GetGuid() == recurringId);
        Assert.Equal(court.Id, match.GetProperty("courtId").GetGuid());
        Assert.Equal("Cliente fijo para listar", match.GetProperty("customerName").GetString());
        Assert.Equal(weekday, match.GetProperty("weekday").GetInt32());
    }

    [Fact]
    public async Task Create_CanchaInexistente_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/recurring-bookings", new
        {
            courtId = Guid.NewGuid(),
            customerName = "Cliente fijo",
            customerPhone = "1122334455",
            weekday = (int)DateTime.Now.DayOfWeek,
            startHour = 20,
            endHour = 21,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("La cancha indicada no existe", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Delete_CancelaLasReservasFuturasGeneradas()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var weekday = (int)DateTime.Now.DayOfWeek;

        var createResponse = await client.PostAsJsonAsync("/api/recurring-bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente fijo",
            customerPhone = "1122334455",
            weekday,
            startHour = 21,
            endHour = 22,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var recurringId = created.GetProperty("id").GetGuid();

        var deleteResponse = await client.DeleteAsync($"/api/recurring-bookings/{recurringId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generated = await db.Bookings.Where(b => b.RecurringBookingId == recurringId).ToListAsync();

        Assert.Equal(8, generated.Count);
        Assert.All(generated, b => Assert.Equal("cancelled", b.Status));

        var listResponse = await client.GetAsync("/api/recurring-bookings");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(list.EnumerateArray(), r => r.GetProperty("id").GetGuid() == recurringId);
    }
}
