using System.Linq;
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
    public async Task AddPayment_ConMontoCeroONegativo_Devuelve400()
    {
        var booking = SeedBooking(totalAmount: 3000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new
        {
            amount = 0m,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task AddPayment_ConPaymentMethodInvalido_Devuelve400()
    {
        var booking = SeedBooking(totalAmount: 3000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new
        {
            amount = 1000m,
            paymentMethod = "bitcoin",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task AddPayment_ConReservaInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync($"/api/bookings/{Guid.NewGuid()}/payments", new
        {
            amount = 1000m,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task GetDebts_IncluyeReservasConSaldoPendienteYExcluyeLasPagadas()
    {
        var conDeuda = SeedBooking(totalAmount: 2500m);
        var pagada = SeedBooking(totalAmount: 1000m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trackedPagada = db.Bookings.Single(b => b.Id == pagada.Id);
            trackedPagada.PaidAmount = 1000m;
            trackedPagada.PaymentStatus = "paid";
            db.SaveChanges();
        }

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/payments/debts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var debts = body.EnumerateArray().ToList();

        var deudaDelTest = debts.SingleOrDefault(d => d.GetProperty("bookingId").GetGuid() == conDeuda.Id);
        Assert.True(deudaDelTest.ValueKind == JsonValueKind.Object);
        Assert.Equal(2500m, deudaDelTest.GetProperty("amountDue").GetDecimal());
        Assert.Equal(conDeuda.CustomerName, deudaDelTest.GetProperty("customerName").GetString());

        Assert.DoesNotContain(debts, d => d.GetProperty("bookingId").GetGuid() == pagada.Id);
    }

    [Fact]
    public async Task GetDebts_ComoEmpleado_Devuelve200()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/payments/debts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSummary_SumaPorMedioDePagoIncluyendoVentasDeProductos()
    {
        var fecha = new DateTime(2021, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var booking = SeedBooking(totalAmount: 5000m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.BookingPayments.Add(new BookingPayment
            {
                BookingId = booking.Id,
                Amount = 1000m,
                PaymentMethod = "efectivo",
                CreatedAt = fecha,
            });
            db.BookingPayments.Add(new BookingPayment
            {
                BookingId = booking.Id,
                Amount = 2000m,
                PaymentMethod = "transferencia",
                CreatedAt = fecha,
            });

            var product = new Product { Name = "Pelotas de test", Type = "venta", Stock = 10, MinStock = 2, Price = 500m };
            db.Products.Add(product);
            db.ProductSales.Add(new ProductSale
            {
                ProductId = product.Id,
                Quantity = 1,
                Amount = 500m,
                PaymentMethod = "tarjeta",
                CreatedAt = fecha,
            });

            db.SaveChanges();
        }

        var client = await AuthenticatedClientAsync("admin", "Admin123!");
        var response = await client.GetAsync("/api/payments/summary?date=2021-03-10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("2021-03-10", body.GetProperty("date").GetString());
        Assert.Equal(1000m, body.GetProperty("cash").GetDecimal());
        Assert.Equal(2000m, body.GetProperty("transfer").GetDecimal());
        Assert.Equal(500m, body.GetProperty("card").GetDecimal());
        Assert.Equal(3500m, body.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task GetPayments_DevuelveLosPagosDeLaReserva()
    {
        var booking = SeedBooking(totalAmount: 4000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new { amount = 1500m, paymentMethod = "efectivo" });
        await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new { amount = 2500m, paymentMethod = "tarjeta" });

        var response = await client.GetAsync($"/api/bookings/{booking.Id}/payments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var payments = body.EnumerateArray().ToList();

        Assert.Equal(2, payments.Count);
        Assert.Equal(1500m, payments[0].GetProperty("amount").GetDecimal());
        Assert.Equal(2500m, payments[1].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task EditPayment_CorrigeMontoYMedioDePagoYRecalculaLaReserva()
    {
        var booking = SeedBooking(totalAmount: 4000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new { amount = 2000m, paymentMethod = "efectivo" });

        var paymentsResponse = await client.GetAsync($"/api/bookings/{booking.Id}/payments");
        var payments = (await paymentsResponse.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var paymentId = payments[0].GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/bookings/{booking.Id}/payments/{paymentId}", new
        {
            amount = 4000m,
            paymentMethod = "transferencia",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4000m, body.GetProperty("paidAmount").GetDecimal());
        Assert.Equal("paid", body.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task EditPayment_ConPaymentIdInexistente_Devuelve404()
    {
        var booking = SeedBooking(totalAmount: 3000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PutAsJsonAsync($"/api/bookings/{booking.Id}/payments/{Guid.NewGuid()}", new
        {
            amount = 1000m,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletePayment_DescuentaElMontoYVuelveAQuedarPending()
    {
        var booking = SeedBooking(totalAmount: 3000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/payments", new { amount = 3000m, paymentMethod = "efectivo" });

        var paymentsResponse = await client.GetAsync($"/api/bookings/{booking.Id}/payments");
        var payments = (await paymentsResponse.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var paymentId = payments[0].GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"/api/bookings/{booking.Id}/payments/{paymentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, body.GetProperty("paidAmount").GetDecimal());
        Assert.Equal("pending", body.GetProperty("paymentStatus").GetString());

        var remaining = await client.GetAsync($"/api/bookings/{booking.Id}/payments");
        var remainingBody = await remaining.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(remainingBody.EnumerateArray());
    }

    [Fact]
    public async Task GetSummary_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/payments/summary?date=2021-03-10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSummary_ConFechaInvalida_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/payments/summary?date=no-es-una-fecha");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }
}
