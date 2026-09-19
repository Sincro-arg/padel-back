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
    public async Task Update_ComoAdmin_EditaDatosYDevuelve200()
    {
        var member = SeedMember("Socio A Editar");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/members/{member.Id}", new
        {
            name = "Socio Editado",
            phone = "1199887766",
            membershipFee = 15000,
            discountPercent = 20,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Socio Editado", body.GetProperty("name").GetString());
        Assert.Equal("1199887766", body.GetProperty("phone").GetString());
        Assert.Equal(15000m, body.GetProperty("membershipFee").GetDecimal());
        Assert.Equal(20m, body.GetProperty("discountPercent").GetDecimal());
    }

    [Fact]
    public async Task Update_ConNombreVacio_Devuelve400()
    {
        var member = SeedMember("Socio Test Update Invalido");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/members/{member.Id}", new
        {
            name = "",
            phone = "1122334455",
            membershipFee = 10000,
            discountPercent = 10,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ConIdInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/members/{Guid.NewGuid()}", new
        {
            name = "Socio Inexistente",
            phone = "1122334455",
            membershipFee = 10000,
            discountPercent = 10,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
