using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Tests;

public class DashboardControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public DashboardControllerTests(PadelApiFactory factory)
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

    private Booking SeedBooking(Guid courtId, DateOnly date, int startHour, int endHour, string status = "confirmed",
        decimal totalAmount = 0m, decimal paidAmount = 0m, decimal? cancellationFee = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var booking = new Booking
        {
            CourtId = courtId,
            CustomerName = "Cliente de prueba",
            CustomerPhone = "1122334455",
            Date = date,
            StartHour = startHour,
            EndHour = endHour,
            Status = status,
            TotalAmount = totalAmount,
            PaidAmount = paidAmount,
            PaymentStatus = "pending",
            CancellationFee = cancellationFee,
        };
        db.Bookings.Add(booking);
        db.SaveChanges();
        return booking;
    }

    private Member SeedMember(DateOnly joinedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var member = new Member
        {
            Name = $"Socio de test {Guid.NewGuid()}",
            Phone = "1122334455",
            MembershipFee = 10000m,
            DiscountPercent = 0m,
            JoinedAt = joinedAt,
        };
        db.Members.Add(member);
        db.SaveChanges();
        return member;
    }

    [Fact]
    public async Task GetToday_ConReservaQueCubreLaHoraActual_MarcaLaCanchaOccupiedYLaOtraFree()
    {
        var currentHour = DateTime.Now.Hour;
        var today = DateOnly.FromDateTime(DateTime.Now);

        var courtOcupada = SeedCourt();
        var courtLibre = SeedCourt();
        var booking = SeedBooking(courtOcupada.Id, today, currentHour, currentHour + 1);

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/dashboard/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var courts = body.GetProperty("courts").EnumerateArray().ToList();

        var dtoOcupada = courts.Single(c => c.GetProperty("courtId").GetGuid() == courtOcupada.Id);
        Assert.Equal("occupied", dtoOcupada.GetProperty("status").GetString());
        Assert.Equal(booking.Id, dtoOcupada.GetProperty("currentBooking").GetProperty("id").GetGuid());

        var dtoLibre = courts.Single(c => c.GetProperty("courtId").GetGuid() == courtLibre.Id);
        Assert.Equal("free", dtoLibre.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, dtoLibre.GetProperty("currentBooking").ValueKind);
    }

    [Fact]
    public async Task GetToday_TodayBookings_QuedanOrdenadosPorStartHour()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var court = SeedCourt();

        var tarde = SeedBooking(court.Id, today, 20, 21);
        var temprano = SeedBooking(court.Id, today, 9, 10);
        var mediodia = SeedBooking(court.Id, today, 13, 14);

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/dashboard/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("todayBookings").EnumerateArray()
            .Select(b => b.GetProperty("id").GetGuid())
            .ToList();

        var indiceTemprano = ids.IndexOf(temprano.Id);
        var indiceMediodia = ids.IndexOf(mediodia.Id);
        var indiceTarde = ids.IndexOf(tarde.Id);

        Assert.True(indiceTemprano < indiceMediodia);
        Assert.True(indiceMediodia < indiceTarde);
    }

    [Fact]
    public async Task GetToday_IncomeToday_SumaPagosDeReservasYVentasDeProductosDeHoy()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var antes = await client.GetAsync("/api/dashboard/today");
        var incomeAntes = (await antes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("incomeToday").GetDecimal();

        var court = SeedCourt();
        var booking = SeedBooking(court.Id, DateOnly.FromDateTime(DateTime.Now), 10, 11, totalAmount: 5000m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BookingPayments.Add(new BookingPayment
            {
                BookingId = booking.Id,
                Amount = 1500m,
                PaymentMethod = "efectivo",
                CreatedAt = DateTime.Now,
            });

            var product = new Product { Name = $"Producto de test {Guid.NewGuid()}", Type = "venta", Stock = 10, MinStock = 2, Price = 500m };
            db.Products.Add(product);
            db.ProductSales.Add(new ProductSale
            {
                ProductId = product.Id,
                Quantity = 1,
                Amount = 500m,
                PaymentMethod = "tarjeta",
                CreatedAt = DateTime.Now,
            });
            db.SaveChanges();
        }

        var despues = await client.GetAsync("/api/dashboard/today");
        var incomeDespues = (await despues.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("incomeToday").GetDecimal();

        Assert.Equal(2000m, incomeDespues - incomeAntes);
    }

    [Fact]
    public async Task GetToday_DebtTotal_SumaLaDeudaPendienteDeReservasConfirmadasYCanceladas()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var antes = await client.GetAsync("/api/dashboard/today");
        var debtAntes = (await antes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("debtTotal").GetDecimal();

        var court = SeedCourt();
        var today = DateOnly.FromDateTime(DateTime.Now);
        // Confirmada con saldo pendiente: debe totalAmount - paidAmount.
        SeedBooking(court.Id, today, 8, 9, status: "confirmed", totalAmount: 3000m, paidAmount: 1000m);
        // Cancelada con penalidad pendiente de cobro.
        SeedBooking(court.Id, today, 12, 13, status: "cancelled", totalAmount: 4000m, paidAmount: 0m, cancellationFee: 800m);

        var despues = await client.GetAsync("/api/dashboard/today");
        var debtDespues = (await despues.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("debtTotal").GetDecimal();

        Assert.Equal(2800m, debtDespues - debtAntes);
    }

    [Fact]
    public async Task GetToday_MembersLate_IncluyeSocioQueDebeUnMesYExcluyeAlQuePagoElMesEnCurso()
    {
        var hoy = DateTime.UtcNow;
        var socioMoroso = SeedMember(DateOnly.FromDateTime(hoy));
        var socioAlDia = SeedMember(DateOnly.FromDateTime(hoy));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MemberPayments.Add(new MemberPayment
            {
                MemberId = socioAlDia.Id,
                Month = hoy.Month,
                Year = hoy.Year,
                Amount = 10000m,
                PaymentMethod = "efectivo",
            });
            db.SaveChanges();
        }

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/dashboard/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var membersLate = body.GetProperty("membersLate").EnumerateArray().ToList();

        var dtoMoroso = membersLate.Single(m => m.GetProperty("id").GetGuid() == socioMoroso.Id);
        Assert.Equal(1, dtoMoroso.GetProperty("monthsOwed").GetInt32());

        Assert.DoesNotContain(membersLate, m => m.GetProperty("id").GetGuid() == socioAlDia.Id);
    }
}
