using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

    private Court SeedCourt(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var court = new Court { Name = name };
        db.Courts.Add(court);
        db.SaveChanges();
        return court;
    }

    private Booking SeedBooking(Guid courtId, int startHour, int endHour, string customerName, string status = "confirmed")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var booking = new Booking
        {
            CourtId = courtId,
            CustomerName = customerName,
            CustomerPhone = "1122334455",
            Date = DateOnly.FromDateTime(DateTime.Now),
            StartHour = startHour,
            EndHour = endHour,
            Status = status,
            TotalAmount = 5000m,
            PaidAmount = 0,
            PaymentStatus = "pending",
        };
        db.Bookings.Add(booking);
        db.SaveChanges();
        return booking;
    }

    private Member SeedMember(string name, DateOnly joinedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var member = new Member
        {
            Name = name,
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
    public async Task GetToday_CanchaConReservaEnLaHoraActual_QuedaOccupied()
    {
        var currentHour = DateTime.Now.Hour;
        var court = SeedCourt($"Ocupada {Guid.NewGuid()}");
        SeedBooking(court.Id, currentHour, currentHour + 1, "Cliente Ocupado");

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/dashboard/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var courtDto = body.GetProperty("courts").EnumerateArray()
            .First(c => c.GetProperty("courtId").GetGuid() == court.Id);

        Assert.Equal("occupied", courtDto.GetProperty("status").GetString());
        Assert.Equal("Cliente Ocupado", courtDto.GetProperty("currentBooking").GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task GetToday_CanchaSinReservaEnLaHoraActual_QuedaFree()
    {
        var currentHour = DateTime.Now.Hour;
        var court = SeedCourt($"Libre {Guid.NewGuid()}");
        // Reserva que no cubre la hora actual.
        SeedBooking(court.Id, currentHour + 5, currentHour + 6, "Cliente Otro Horario");

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/dashboard/today");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var courtDto = body.GetProperty("courts").EnumerateArray()
            .First(c => c.GetProperty("courtId").GetGuid() == court.Id);

        Assert.Equal("free", courtDto.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, courtDto.GetProperty("currentBooking").ValueKind);
    }

    [Fact]
    public async Task GetToday_ReservaCancelada_NoCuentaComoOcupada()
    {
        var currentHour = DateTime.Now.Hour;
        var court = SeedCourt($"Cancelada {Guid.NewGuid()}");
        SeedBooking(court.Id, currentHour, currentHour + 1, "Cliente Cancelado", status: "cancelled");

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/dashboard/today");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var courtDto = body.GetProperty("courts").EnumerateArray()
            .First(c => c.GetProperty("courtId").GetGuid() == court.Id);

        Assert.Equal("free", courtDto.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetToday_TodayBookings_QuedanOrdenadosPorStartHour()
    {
        var court = SeedCourt($"Orden {Guid.NewGuid()}");
        SeedBooking(court.Id, 20, 21, "Cliente Tarde");
        SeedBooking(court.Id, 9, 10, "Cliente Temprano");

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/dashboard/today");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var names = body.GetProperty("todayBookings").EnumerateArray()
            .Select(b => b.GetProperty("customerName").GetString())
            .Where(n => n == "Cliente Tarde" || n == "Cliente Temprano")
            .ToList();

        Assert.Equal(new[] { "Cliente Temprano", "Cliente Tarde" }, names);
    }

    [Fact]
    public async Task GetToday_SocioAtrasado_ApareceEnMembersLate()
    {
        var joinedAt = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
        var member = SeedMember("Socio Atrasado", joinedAt);

        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var response = await client.GetAsync("/api/dashboard/today");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var late = body.GetProperty("membersLate").EnumerateArray()
            .First(m => m.GetProperty("id").GetGuid() == member.Id);

        Assert.Equal(3, late.GetProperty("monthsOwed").GetInt32());
    }

    [Fact]
    public async Task GetToday_ComoAdminYComoEmpleado_DevuelveOk()
    {
        var adminClient = await AuthenticatedClientAsync("admin", "Admin123!");
        var empleadoClient = await AuthenticatedClientAsync("empleado", "Empleado123!");

        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/dashboard/today")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await empleadoClient.GetAsync("/api/dashboard/today")).StatusCode);
    }
}
