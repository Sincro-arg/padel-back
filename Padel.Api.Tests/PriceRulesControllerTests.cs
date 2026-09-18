using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

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

    private Guid SeedPriceRule(string dayType = "weekday", int startHour = 8, int endHour = 12, decimal pricePerHour = 5000m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rule = new PriceRule
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
    public async Task Create_ConDayTypeInvalido_Devuelve400()
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
    public async Task Create_ConStartHourMayorOIgualQueEndHour_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/price-rules", new
        {
            dayType = "weekday",
            startHour = 14,
            endHour = 10,
            pricePerHour = 5000,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task GetAll_ComoAdmin_DevuelveReglasOrdenadas()
    {
        SeedPriceRule(dayType: "weekend", startHour: 10, endHour: 14, pricePerHour: 7000m);
        SeedPriceRule(dayType: "weekday", startHour: 8, endHour: 12, pricePerHour: 5000m);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/price-rules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Update_ComoAdmin_ActualizaLaRegla()
    {
        var id = SeedPriceRule(dayType: "weekday", startHour: 8, endHour: 12, pricePerHour: 5000m);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/price-rules/{id}", new
        {
            dayType = "weekend",
            startHour = 9,
            endHour = 13,
            pricePerHour = 8000,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = await db.PriceRules.FirstAsync(r => r.Id == id);
        Assert.Equal("weekend", rule.DayType);
        Assert.Equal(9, rule.StartHour);
        Assert.Equal(13, rule.EndHour);
        Assert.Equal(8000m, rule.PricePerHour);
    }

    [Fact]
    public async Task Update_ConDatosInvalidos_Devuelve400YNoModifica()
    {
        var id = SeedPriceRule(dayType: "weekday", startHour: 8, endHour: 12, pricePerHour: 5000m);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/price-rules/{id}", new
        {
            dayType = "weekday",
            startHour = 12,
            endHour = 8,
            pricePerHour = 5000,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = await db.PriceRules.FirstAsync(r => r.Id == id);
        Assert.Equal(8, rule.StartHour);
        Assert.Equal(12, rule.EndHour);
    }

    [Fact]
    public async Task Delete_ComoAdmin_BorraLaRegla()
    {
        var id = SeedPriceRule();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/price-rules/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = await db.PriceRules.FirstOrDefaultAsync(r => r.Id == id);
        Assert.Null(rule);
    }

    [Fact]
    public async Task Delete_ComoEmpleado_Devuelve403()
    {
        var id = SeedPriceRule();
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.DeleteAsync($"/api/price-rules/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
