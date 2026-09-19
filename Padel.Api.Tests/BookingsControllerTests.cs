using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Tests;

public class BookingsControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public BookingsControllerTests(PadelApiFactory factory)
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

    // Usa las PriceRules que ya trae el seed (weekday 8-17 a $4000/hora): así no
    // colisiona con ellas agregando una regla propia que se superponga.
    private Court SeedCourt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var court = new Court { Name = $"Cancha de test {Guid.NewGuid()}" };
        db.Courts.Add(court);
        db.SaveChanges();
        return court;
    }

    // Próximo lunes: así el test no depende del día en que se corra.
    private static string NextWeekday()
    {
        var date = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(1);
        return date.ToString("yyyy-MM-dd");
    }

    [Fact]
    public async Task Create_CalculaElPrecioSegunPriceRules()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        var response = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente de prueba",
            customerPhone = "1122334455",
            date,
            startHour = 10,
            endHour = 12,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Seed: weekday 8-17hs a $4000/hora -> 2 horas = $8000.
        Assert.Equal(8000m, body.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("confirmed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Create_ConHorarioSolapado_Devuelve409()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Primer cliente",
            customerPhone = "1111111111",
            date,
            startHour = 14,
            endHour = 16,
        });

        var response = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Segundo cliente",
            customerPhone = "2222222222",
            date,
            startHour = 15,
            endHour = 17,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_ConMasDeCuatroHorasDeAnticipacion_NoCobraPenalidad()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = DateOnly.FromDateTime(DateTime.Now.AddDays(3)).ToString("yyyy-MM-dd");

        var createResponse = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente de prueba",
            customerPhone = "1122334455",
            date,
            startHour = 9,
            endHour = 10,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/bookings/{id}/cancel", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cancelled", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("cancellationFee").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Cancel_ConMenosDeCuatroHorasDeAnticipacion_CobraLaMitadComoPenalidad()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        // Inserta la reserva directo en la base con fecha ya pasada, para que
        // "faltan menos de 4hs" se cumpla siempre sin depender de price rules
        // ni de la hora/día en que corra el test.
        Guid id;
        decimal totalAmount = 8000m;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var booking = new Booking
            {
                CourtId = court.Id,
                CustomerName = "Cliente de prueba",
                CustomerPhone = "1122334455",
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
                StartHour = 10,
                EndHour = 11,
                Status = "confirmed",
                TotalAmount = totalAmount,
                PaidAmount = 0,
                PaymentStatus = "pending",
            };
            db.Bookings.Add(booking);
            db.SaveChanges();
            id = booking.Id;
        }

        var response = await client.PostAsJsonAsync($"/api/bookings/{id}/cancel", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cancelled", body.GetProperty("status").GetString());
        Assert.Equal(totalAmount * 0.5m, body.GetProperty("cancellationFee").GetDecimal());
        Assert.Equal("pending", body.GetProperty("paymentStatus").GetString());
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
    public async Task Create_ConSocioQueDebeDosOMasCuotas_Devuelve403()
    {
        var court = SeedCourt();
        // Se dio de alta hace 2 meses y nunca pagó: debe 3 cuotas.
        var member = SeedMember("Socio Moroso", DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2));
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        var response = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Socio Moroso",
            customerPhone = "1122334455",
            date,
            startHour = 10,
            endHour = 11,
            memberId = member.Id,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "El socio debe 2 o más cuotas y no puede reservar hasta ponerse al día",
            body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_ConSocioAlDia_PermiteLaReserva()
    {
        var court = SeedCourt();
        var member = SeedMember("Socio Al Dia", DateOnly.FromDateTime(DateTime.UtcNow));
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        var response = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Socio Al Dia",
            customerPhone = "1122334455",
            date,
            startHour = 11,
            endHour = 12,
            memberId = member.Id,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConSocioConDescuento_AplicaElDescuentoAlTotal()
    {
        var court = SeedCourt();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var member = new Member
            {
                Name = "Socio Con Descuento",
                Phone = "1122334455",
                MembershipFee = 10000m,
                DiscountPercent = 20m,
                JoinedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Members.Add(member);
            db.SaveChanges();

            var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
            var date = NextWeekday();

            var response = await client.PostAsJsonAsync("/api/bookings", new
            {
                courtId = court.Id,
                customerName = "Socio Con Descuento",
                customerPhone = "1122334455",
                date,
                startHour = 10,
                endHour = 12,
                memberId = member.Id,
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            // Seed: weekday 8-17hs a $4000/hora -> 2 horas = $8000, con 20% off = $6400.
            Assert.Equal(6400m, body.GetProperty("totalAmount").GetDecimal());
        }
    }

    [Fact]
    public async Task Update_EditaCanchaHorarioYClienteYRecalculaElTotal()
    {
        var court = SeedCourt();
        var otherCourt = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        var createResponse = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente original",
            customerPhone = "1111111111",
            date,
            startHour = 10,
            endHour = 11,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/bookings/{id}", new
        {
            courtId = otherCourt.Id,
            customerName = "Cliente editado",
            customerPhone = "2222222222",
            date,
            startHour = 10,
            endHour = 12,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(otherCourt.Id, body.GetProperty("courtId").GetGuid());
        Assert.Equal("Cliente editado", body.GetProperty("customerName").GetString());
        Assert.Equal("2222222222", body.GetProperty("customerPhone").GetString());
        Assert.Equal(12, body.GetProperty("endHour").GetInt32());
        // Seed: weekday 8-17hs a $4000/hora -> ahora 2 horas = $8000.
        Assert.Equal(8000m, body.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Update_EditandoseASiMismaEnElMismoHorario_NoDevuelve409()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        var createResponse = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente de prueba",
            customerPhone = "1122334455",
            date,
            startHour = 10,
            endHour = 11,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/bookings/{id}", new
        {
            courtId = court.Id,
            customerName = "Cliente de prueba, nombre corregido",
            customerPhone = "1122334455",
            date,
            startHour = 10,
            endHour = 11,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_ConHorarioSolapadoContraOtraReserva_Devuelve409()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Primer cliente",
            customerPhone = "1111111111",
            date,
            startHour = 14,
            endHour = 16,
        });

        var createResponse = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Segundo cliente",
            customerPhone = "2222222222",
            date,
            startHour = 18,
            endHour = 19,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/bookings/{id}", new
        {
            courtId = court.Id,
            customerName = "Segundo cliente",
            customerPhone = "2222222222",
            date,
            startHour = 15,
            endHour = 17,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("La cancha ya tiene una reserva en ese horario", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Update_ConIdInexistente_Devuelve404()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        var response = await client.PutAsJsonAsync($"/api/bookings/{Guid.NewGuid()}", new
        {
            courtId = court.Id,
            customerName = "Cliente de prueba",
            customerPhone = "1122334455",
            date,
            startHour = 10,
            endHour = 11,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_BorraLaReserva()
    {
        var court = SeedCourt();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");
        var date = NextWeekday();

        var createResponse = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = court.Id,
            customerName = "Cliente de prueba",
            customerPhone = "1122334455",
            date,
            startHour = 20,
            endHour = 21,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var deleteResponse = await client.DeleteAsync($"/api/bookings/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/bookings?date={date}");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(list.EnumerateArray(), b => b.GetProperty("id").GetGuid() == id);
    }
}
