using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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
}
