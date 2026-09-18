using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;

namespace Padel.Api.Tests;

public class CourtsControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public CourtsControllerTests(PadelApiFactory factory)
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

    private Guid SeedCourt(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var court = new Padel.Api.Models.Court { Name = name };
        db.Courts.Add(court);
        db.SaveChanges();

        return court.Id;
    }

    [Fact]
    public async Task GetAll_ComoEmpleado_Devuelve200ConLista()
    {
        SeedCourt("Cancha de test GET");
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/courts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetAll_SinToken_Devuelve401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/courts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ComoAdmin_DevuelveCreado()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/courts", new { name = "Cancha nueva" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancha nueva", body.GetProperty("name").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Create_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/courts", new { name = "Cancha de empleado" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_SinNombre_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/courts", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Update_ComoAdmin_DevuelveActualizado()
    {
        var id = SeedCourt("Cancha para actualizar");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/courts/{id}", new { name = "Cancha actualizada" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancha actualizada", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_ComoEmpleado_Devuelve403()
    {
        var id = SeedCourt("Cancha que empleado no puede tocar");
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PutAsJsonAsync($"/api/courts/{id}", new { name = "Intento de empleado" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_IdInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/courts/{Guid.NewGuid()}", new { name = "No existe" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_SinNombre_Devuelve400()
    {
        var id = SeedCourt("Cancha para update sin nombre");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/courts/{id}", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ComoAdmin_Devuelve204YLaSaca()
    {
        var id = SeedCourt("Cancha para borrar");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/courts/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Courts.AnyAsync(c => c.Id == id));
    }

    [Fact]
    public async Task Delete_ComoEmpleado_Devuelve403()
    {
        var id = SeedCourt("Cancha que empleado no puede borrar");
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.DeleteAsync($"/api/courts/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_IdInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/courts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
