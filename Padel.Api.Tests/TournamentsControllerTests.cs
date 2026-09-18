using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Tests;

// Parte 1/2 del controller: torneos y parejas (GetAll, Create, Update, Delete,
// GetPairs, CreatePair, DeletePair). Matches y su resultado quedan para la parte 2.
public class TournamentsControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public TournamentsControllerTests(PadelApiFactory factory)
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

    private Guid SeedTournament(string name = "Torneo Apertura", string date = "2026-01-15", decimal registrationFee = 10000m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tournament = new Tournament
        {
            Name = name,
            Date = DateOnly.Parse(date),
            RegistrationFee = registrationFee,
        };
        db.Tournaments.Add(tournament);
        db.SaveChanges();

        return tournament.Id;
    }

    private Guid SeedPair(Guid tournamentId, string player1 = "Juan", string player2 = "Pedro", bool paid = false, string? paymentMethod = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pair = new Pair
        {
            TournamentId = tournamentId,
            Player1 = player1,
            Player2 = player2,
            Paid = paid,
            PaymentMethod = paymentMethod,
        };
        db.Pairs.Add(pair);
        db.SaveChanges();

        return pair.Id;
    }

    [Fact]
    public async Task GetAll_ComoAdmin_DevuelveTorneosOrdenadosPorFecha()
    {
        SeedTournament(name: "Torneo B", date: "2026-03-01");
        SeedTournament(name: "Torneo A", date: "2026-02-01");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/tournaments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task GetAll_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/tournaments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ComoAdmin_DevuelveCreado()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Torneo Clausura",
            date = "2026-06-20",
            registrationFee = 8000,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Torneo Clausura", body.GetProperty("name").GetString());
        Assert.Equal("2026-06-20", body.GetProperty("date").GetString());
    }

    [Fact]
    public async Task Create_ConFechaInvalida_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Torneo Clausura",
            date = "fecha-invalida",
            registrationFee = 8000,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Update_ComoAdmin_ActualizaElTorneo()
    {
        var id = SeedTournament(name: "Torneo Viejo", date: "2026-01-15", registrationFee: 5000m);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/tournaments/{id}", new
        {
            name = "Torneo Nuevo",
            date = "2026-02-20",
            registrationFee = 9000,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tournament = await db.Tournaments.FirstAsync(t => t.Id == id);
        Assert.Equal("Torneo Nuevo", tournament.Name);
        Assert.Equal(new DateOnly(2026, 2, 20), tournament.Date);
        Assert.Equal(9000m, tournament.RegistrationFee);
    }

    [Fact]
    public async Task Delete_ComoAdmin_BorraElTorneo()
    {
        var id = SeedTournament();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/tournaments/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tournament = await db.Tournaments.FirstOrDefaultAsync(t => t.Id == id);
        Assert.Null(tournament);
    }

    [Fact]
    public async Task GetPairs_ComoAdmin_DevuelveLasParejasDelTorneo()
    {
        var tournamentId = SeedTournament();
        SeedPair(tournamentId, player1: "Juan", player2: "Pedro");
        SeedPair(tournamentId, player1: "Ana", player2: "Sol");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync($"/api/tournaments/{tournamentId}/pairs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetArrayLength());
    }

    [Fact]
    public async Task CreatePair_ComoAdmin_DevuelveCreada()
    {
        var tournamentId = SeedTournament();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/pairs", new
        {
            player1 = "Juan",
            player2 = "Pedro",
            paid = true,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Juan", body.GetProperty("player1").GetString());
        Assert.Equal("Pedro", body.GetProperty("player2").GetString());
        Assert.True(body.GetProperty("paid").GetBoolean());
    }

    [Fact]
    public async Task CreatePair_ConJugadorFaltante_Devuelve400()
    {
        var tournamentId = SeedTournament();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/pairs", new
        {
            player1 = "Juan",
            player2 = "",
            paid = false,
            paymentMethod = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task CreatePair_ConPaymentMethodInvalido_Devuelve400()
    {
        var tournamentId = SeedTournament();
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/pairs", new
        {
            player1 = "Juan",
            player2 = "Pedro",
            paid = true,
            paymentMethod = "cheque",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task DeletePair_ComoAdmin_BorraLaPareja()
    {
        var tournamentId = SeedTournament();
        var pairId = SeedPair(tournamentId);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/tournaments/pairs/{pairId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pair = await db.Pairs.FirstOrDefaultAsync(p => p.Id == pairId);
        Assert.Null(pair);
    }

    [Fact]
    public async Task DeletePair_ComoEmpleado_Devuelve403()
    {
        var tournamentId = SeedTournament();
        var pairId = SeedPair(tournamentId);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.DeleteAsync($"/api/tournaments/pairs/{pairId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
