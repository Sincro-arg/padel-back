using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Tests;

public class ReportsControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public ReportsControllerTests(PadelApiFactory factory)
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

    private (Guid CourtId, Guid BookingId) SeedConfirmedBooking(
        DateOnly date, int startHour, int endHour, string customerName, decimal totalAmount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var court = new Court { Name = $"Cancha de test {Guid.NewGuid()}" };
        db.Courts.Add(court);

        var booking = new Booking
        {
            CourtId = court.Id,
            CustomerName = customerName,
            CustomerPhone = "1122334455",
            Date = date,
            StartHour = startHour,
            EndHour = endHour,
            Status = "confirmed",
            TotalAmount = totalAmount,
        };
        db.Bookings.Add(booking);
        db.SaveChanges();

        return (court.Id, booking.Id);
    }

    [Fact]
    public async Task GetMonthly_ComoAdmin_SumaFacturacionYAgrupaClientes()
    {
        var date = new DateOnly(2025, 3, 10);
        SeedConfirmedBooking(date, 9, 10, "Juan Pérez", 5000);
        SeedConfirmedBooking(date, 10, 11, "Juan Pérez", 5000);
        SeedConfirmedBooking(date, 14, 15, "María López", 4000);

        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/reports/monthly?year=2025&month=03");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(14000, body.GetProperty("totalRevenue").GetDecimal());

        var topCustomers = body.GetProperty("topCustomers").EnumerateArray().ToList();
        Assert.Equal("Juan Pérez", topCustomers[0].GetProperty("name").GetString());
        Assert.Equal(10000, topCustomers[0].GetProperty("totalSpent").GetDecimal());
        Assert.Equal(2, topCustomers[0].GetProperty("bookingsCount").GetInt32());

        var byHour = body.GetProperty("byHour").EnumerateArray().ToList();
        Assert.Equal(16, byHour.Count); // horas 8 a 23
        Assert.Equal(8, byHour[0].GetProperty("hour").GetInt32());
    }

    [Fact]
    public async Task GetMonthly_IgnoraReservasCanceladasYDeOtroMes()
    {
        // Año/mes propios de este test (2031-01) para no compartir datos con los
        // demás tests de la clase: el factory (y su base InMemory) es el mismo
        // para todos los [Fact] de acá.
        SeedConfirmedBooking(new DateOnly(2031, 2, 5), 9, 10, "Otro Mes", 9999);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var court = new Court { Name = $"Cancha de test {Guid.NewGuid()}" };
            db.Courts.Add(court);
            db.Bookings.Add(new Booking
            {
                CourtId = court.Id,
                CustomerName = "Cancelado",
                CustomerPhone = "1122334455",
                Date = new DateOnly(2031, 1, 10),
                StartHour = 9,
                EndHour = 10,
                Status = "cancelled",
                TotalAmount = 8888,
            });
            db.SaveChanges();
        }

        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/reports/monthly?year=2031&month=01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("totalRevenue").GetDecimal());
        Assert.Empty(body.GetProperty("topCustomers").EnumerateArray());
    }

    [Fact]
    public async Task GetMonthly_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/reports/monthly?year=2025&month=03");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMonthly_ConMesInvalido_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/reports/monthly?year=2025&month=13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }
}
