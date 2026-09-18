using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;

namespace Padel.Api.Tests;

public class PriceRulesControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public PriceRulesControllerTests(PadelApiFactory factory)
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

    private Guid SeedPriceRule(string dayType = "weekday", int startHour = 8, int endHour = 12, decimal pricePerHour = 5000)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rule = new Padel.Api.Models.PriceRule
        {
            DayType = dayType,
            StartHour = startHour,
            EndHour = endHour,
            PricePerHour = pricePerHour,
        };
        db.PriceRules.Add(rule);
        db.SaveChanges();

        return rule.Id;
    }

    [Fact]
    public async Task Create_ComoAdmin_DevuelveCreado()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/price-rules", new
        {
            dayType = "weekday",
            startHour = 8,
            endHour = 12,
            pricePerHour = 5000,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/price-rules", new
        {
            dayType = "weekday",
            startHour = 8,
            endHour = 12,
            pricePerHour = 5000,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_DayTypeInvalido_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/price-rules", new
        {
            dayType = "feriado",
            startHour = 8,
            endHour = 12,
            pricePerHour = 5000,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Create_StartHourMayorOIgualQueEndHour_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/price-rules", new
        {
            dayType = "weekday",
            startHour = 12,
            endHour = 12,
            pricePerHour = 5000,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task GetAll_ComoAdmin_Devuelve200ConLista()
    {
        SeedPriceRule();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/price-rules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetAll_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/price-rules");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_ComoAdmin_DevuelveActualizado()
    {
        var id = SeedPriceRule();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/price-rules/{id}", new
        {
            dayType = "weekend",
            startHour = 9,
            endHour = 13,
            pricePerHour = 6000,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("weekend", body.GetProperty("dayType").GetString());
        Assert.Equal(9, body.GetProperty("startHour").GetInt32());
        Assert.Equal(13, body.GetProperty("endHour").GetInt32());
    }

    [Fact]
    public async Task Update_ComoEmpleado_Devuelve403()
    {
        var id = SeedPriceRule();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PutAsJsonAsync($"/api/price-rules/{id}", new
        {
            dayType = "weekend",
            startHour = 9,
            endHour = 13,
            pricePerHour = 6000,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_IdInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/price-rules/{Guid.NewGuid()}", new
        {
            dayType = "weekday",
            startHour = 8,
            endHour = 12,
            pricePerHour = 5000,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_DatosInvalidos_Devuelve400()
    {
        var id = SeedPriceRule();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/price-rules/{id}", new
        {
            dayType = "weekday",
            startHour = 15,
            endHour = 10,
            pricePerHour = 5000,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ComoAdmin_Devuelve204YLaSaca()
    {
        var id = SeedPriceRule();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/price-rules/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PriceRules.AnyAsync(r => r.Id == id));
    }

    [Fact]
    public async Task Delete_ComoEmpleado_Devuelve403()
    {
        var id = SeedPriceRule();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.DeleteAsync($"/api/price-rules/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_IdInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/price-rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
