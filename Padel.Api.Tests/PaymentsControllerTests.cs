using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Tests;

public class PaymentsControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public PaymentsControllerTests(PadelApiFactory factory)
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

    private Booking SeedBooking(decimal totalAmount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var court = new Court { Name = $"Cancha de test {Guid.NewGuid()}" };
        var booking = new Booking
        {
            CourtId = court.Id,
            CustomerName = "Cliente de prueba",
            CustomerPhone = "1122334455",
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            StartHour = 10,
            EndHour = 11,
            Status = "confirmed",
            TotalAmount = totalAmount,
            PaidAmount = 0,
            PaymentStatus = "pending",
        };

        db.Courts.Add(court);
        db.Bookings.Add(booking);
        db.SaveChanges();

        return booking;
    }

    [Fact]
    public async Task AddPayment_PorElTotal_SaldaLaReservaYQuedaPaid()
    {
        var booking = SeedBooking(totalAmount: 5000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new
        {
            amount = 5000m,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5000m, body.GetProperty("paidAmount").GetDecimal());
        Assert.Equal("paid", body.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task AddPayment_PorLaMitad_QuedaPartial()
    {
        var booking = SeedBooking(totalAmount: 4000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new
        {
            amount = 2000m,
            paymentMethod = "transferencia",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("partial", body.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task EditPayment_CorrigeMontoYMedioDePago_RecalculaLaReserva()
    {
        var booking = SeedBooking(totalAmount: 4000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new
        {
            amount = 1000m,
            paymentMethod = "efectivo",
        });

        var paymentsResponse = await client.GetAsync($"/api/bookings/{booking.Id}/payments");
        var paymentsList = await paymentsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = paymentsList[0].GetProperty("id").GetGuid();

        var editResponse = await client.PutAsJsonAsync($"/api/bookings/{booking.Id}/payments/{paymentId}", new
        {
            amount = 4000m,
            paymentMethod = "transferencia",
        });

        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var edited = await editResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4000m, edited.GetProperty("paidAmount").GetDecimal());
        Assert.Equal("paid", edited.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task DeletePayment_BorraElPagoYVuelveAQuedarPending()
    {
        var booking = SeedBooking(totalAmount: 3000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new
        {
            amount = 3000m,
            paymentMethod = "efectivo",
        });

        var paymentsResponse = await client.GetAsync($"/api/bookings/{booking.Id}/payments");
        var paymentsList = await paymentsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = paymentsList[0].GetProperty("id").GetGuid();

        var deleteResponse = await client.DeleteAsync($"/api/bookings/{booking.Id}/payments/{paymentId}");

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var body = await deleteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, body.GetProperty("paidAmount").GetDecimal());
        Assert.Equal("pending", body.GetProperty("paymentStatus").GetString());
    }
}
