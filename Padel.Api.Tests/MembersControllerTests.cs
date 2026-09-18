using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Tests;

public class MembersControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public MembersControllerTests(PadelApiFactory factory)
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

    private Member SeedMember(string name, DateOnly? joinedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var member = new Member
        {
            Name = name,
            Phone = "1122334455",
            MembershipFee = 10000m,
            DiscountPercent = 10m,
            JoinedAt = joinedAt ?? DateOnly.FromDateTime(DateTime.UtcNow),
        };
        db.Members.Add(member);
        db.SaveChanges();

        return member;
    }

    [Fact]
    public async Task Create_ComoAdmin_DevuelveCreado()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/members", new
        {
            name = "Juan Pérez",
            phone = "1122334455",
            membershipFee = 10000,
            discountPercent = 10,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/members", new
        {
            name = "Juan Pérez",
            phone = "1122334455",
            membershipFee = 10000,
            discountPercent = 10,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_SocioNuevoSinPagos_DebeUnMesYNoEstaBloqueado()
    {
        var member = SeedMember("Nuevo Socio");
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.EnumerateArray().First(m => m.GetProperty("id").GetGuid() == member.Id);

        Assert.Equal(1, dto.GetProperty("monthsOwed").GetInt32());
        Assert.False(dto.GetProperty("isBlocked").GetBoolean());
    }

    [Fact]
    public async Task GetAll_SocioConTresMesesSinPagar_QuedaBloqueado()
    {
        var joinedAt = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
        var member = SeedMember("Socio Moroso", joinedAt);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/members");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.EnumerateArray().First(m => m.GetProperty("id").GetGuid() == member.Id);

        Assert.Equal(3, dto.GetProperty("monthsOwed").GetInt32());
        Assert.True(dto.GetProperty("isBlocked").GetBoolean());
    }

    [Fact]
    public async Task AddPayment_DescuentaElMesDeMonthsOwed()
    {
        var joinedAt = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
        var member = SeedMember("Socio Al Dia", joinedAt);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var today = DateTime.UtcNow;
        var payResponse = await client.PostAsJsonAsync($"/api/members/{member.Id}/payments", new
        {
            month = today.Month,
            year = today.Year,
            amount = 10000,
            paymentMethod = "efectivo",
        });
        Assert.Equal(HttpStatusCode.Created, payResponse.StatusCode);

        var response = await client.GetAsync("/api/members");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.EnumerateArray().First(m => m.GetProperty("id").GetGuid() == member.Id);

        Assert.Equal(1, dto.GetProperty("monthsOwed").GetInt32());
        Assert.False(dto.GetProperty("isBlocked").GetBoolean());
    }

    [Fact]
    public async Task AddPayment_ConMetodoInvalido_Devuelve400()
    {
        var member = SeedMember("Socio Test Pago Invalido");
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var today = DateTime.UtcNow;
        var response = await client.PostAsJsonAsync($"/api/members/{member.Id}/payments", new
        {
            month = today.Month,
            year = today.Year,
            amount = 10000,
            paymentMethod = "cheque",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePayment_CorrigeMontoYMetodo()
    {
        var member = SeedMember("Socio A Corregir");
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var today = DateTime.UtcNow;
        var payResponse = await client.PostAsJsonAsync($"/api/members/{member.Id}/payments", new
        {
            month = today.Month,
            year = today.Year,
            amount = 10000,
            paymentMethod = "efectivo",
        });
        var created = await payResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = created.GetProperty("id").GetGuid();

        var updateResponse = await client.PutAsJsonAsync($"/api/members/{member.Id}/payments/{paymentId}", new
        {
            month = today.Month,
            year = today.Year,
            amount = 12000,
            paymentMethod = "transferencia",
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(12000, updated.GetProperty("amount").GetDecimal());
        Assert.Equal("transferencia", updated.GetProperty("paymentMethod").GetString());
    }

    [Fact]
    public async Task UpdatePayment_ConMetodoInvalido_Devuelve400()
    {
        var member = SeedMember("Socio Pago Update Invalido");
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var today = DateTime.UtcNow;
        var payResponse = await client.PostAsJsonAsync($"/api/members/{member.Id}/payments", new
        {
            month = today.Month,
            year = today.Year,
            amount = 10000,
            paymentMethod = "efectivo",
        });
        var created = await payResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = created.GetProperty("id").GetGuid();

        var updateResponse = await client.PutAsJsonAsync($"/api/members/{member.Id}/payments/{paymentId}", new
        {
            month = today.Month,
            year = today.Year,
            amount = 10000,
            paymentMethod = "cheque",
        });
        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
    }

    [Fact]
    public async Task UpdatePayment_PagoInexistente_Devuelve404()
    {
        var member = SeedMember("Socio Sin Pagos");
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PutAsJsonAsync($"/api/members/{member.Id}/payments/{Guid.NewGuid()}", new
        {
            month = 1,
            year = 2024,
            amount = 1000,
            paymentMethod = "efectivo",
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletePayment_LoElimina_YRestauraMonthsOwed()
    {
        var joinedAt = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
        var member = SeedMember("Socio A Borrar Pago", joinedAt);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var today = DateTime.UtcNow;
        var payResponse = await client.PostAsJsonAsync($"/api/members/{member.Id}/payments", new
        {
            month = today.Month,
            year = today.Year,
            amount = 10000,
            paymentMethod = "efectivo",
        });
        var created = await payResponse.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = created.GetProperty("id").GetGuid();

        var deleteResponse = await client.DeleteAsync($"/api/members/{member.Id}/payments/{paymentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var membersResponse = await client.GetAsync("/api/members");
        var body = await membersResponse.Content.ReadFromJsonAsync<JsonElement>();
        var dto = body.EnumerateArray().First(m => m.GetProperty("id").GetGuid() == member.Id);
        Assert.Equal(2, dto.GetProperty("monthsOwed").GetInt32());
    }

    [Fact]
    public async Task Delete_ComoAdmin_Devuelve204()
    {
        var member = SeedMember("Socio A Borrar");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/members/{member.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
