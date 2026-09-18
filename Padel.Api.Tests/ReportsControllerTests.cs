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

    private Court SeedCourt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var court = new Court { Name = $"Cancha de test {Guid.NewGuid()}" };
        db.Courts.Add(court);
        db.SaveChanges();
        return court;
    }

    private void SeedBooking(Guid courtId, string customerName, DateOnly date, int startHour, int endHour, decimal totalAmount, string status = "confirmed")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Bookings.Add(new Booking
        {
            CourtId = courtId,
            CustomerName = customerName,
            CustomerPhone = "1122334455",
            Date = date,
            StartHour = startHour,
            EndHour = endHour,
            Status = status,
            TotalAmount = totalAmount,
            PaidAmount = 0,
            PaymentStatus = "pending",
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task GetMonthly_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/reports/monthly?year=2021&month=5");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMonthly_ConMesInvalido_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/reports/monthly?year=2021&month=13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMonthly_ComoAdmin_SumaSoloReservasConfirmadasDelMes()
    {
        var court = SeedCourt();
        var date = new DateOnly(2021, 5, 10);

        SeedBooking(court.Id, "Cliente Uno", date, 10, 11, 4000m);
        SeedBooking(court.Id, "Cliente Uno", date, 12, 13, 4000m);
        SeedBooking(court.Id, "Cliente Dos", date, 14, 16, 12000m);
        // Cancelada: no debe sumar al total ni contar como reserva del cliente.
        SeedBooking(court.Id, "Cliente Dos", date, 18, 19, 5000m, status: "cancelled");
        // Otro mes: no debe entrar en el reporte de mayo.
        SeedBooking(court.Id, "Cliente Uno", new DateOnly(2021, 6, 10), 10, 11, 9000m);

        var client = await AuthenticatedClientAsync("admin", "Admin123!");
        var response = await client.GetAsync("/api/reports/monthly?year=2021&month=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(20000m, body.GetProperty("totalRevenue").GetDecimal());

        var byHour = body.GetProperty("byHour").EnumerateArray().ToList();
        Assert.Equal(16, byHour.Count); // horas 8 a 23 inclusive (cierra a las 24)
        Assert.Equal(8, byHour[0].GetProperty("hour").GetInt32());

        var topCustomers = body.GetProperty("topCustomers").EnumerateArray().ToList();
        Assert.Equal("Cliente Dos", topCustomers[0].GetProperty("name").GetString());
        Assert.Equal(12000m, topCustomers[0].GetProperty("totalSpent").GetDecimal());
        Assert.Equal(1, topCustomers[0].GetProperty("bookingsCount").GetInt32());
        Assert.Equal("Cliente Uno", topCustomers[1].GetProperty("name").GetString());
        Assert.Equal(8000m, topCustomers[1].GetProperty("totalSpent").GetDecimal());
        Assert.Equal(2, topCustomers[1].GetProperty("bookingsCount").GetInt32());
    }
}
