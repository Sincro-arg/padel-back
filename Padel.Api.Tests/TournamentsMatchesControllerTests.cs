using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Tests;

// Parte 2/2 del controller: partidos (matches) y carga de resultado.
// La parte 1 (torneos y parejas) vive en TournamentsControllerTests.
public class TournamentsMatchesControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public TournamentsMatchesControllerTests(PadelApiFactory factory)
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

    private Guid SeedPair(Guid tournamentId, string player1 = "Juan", string player2 = "Pedro")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pair = new Pair
        {
            TournamentId = tournamentId,
            Player1 = player1,
            Player2 = player2,
        };
        db.Pairs.Add(pair);
        db.SaveChanges();

        return pair.Id;
    }

    private Guid SeedMatch(Guid tournamentId, Guid pair1Id, Guid pair2Id, string round = "Cuartos")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var match = new Match
        {
            TournamentId = tournamentId,
            Round = round,
            Pair1Id = pair1Id,
            Pair2Id = pair2Id,
        };
        db.Matches.Add(match);
        db.SaveChanges();

        return match.Id;
    }

    [Fact]
    public async Task GetMatches_ComoAdmin_DevuelveLosPartidosDelTorneo()
    {
        var tournamentId = SeedTournament();
        var pair1Id = SeedPair(tournamentId, "Juan", "Pedro");
        var pair2Id = SeedPair(tournamentId, "Ana", "Sol");
        SeedMatch(tournamentId, pair1Id, pair2Id);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync($"/api/tournaments/{tournamentId}/matches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetArrayLength());
    }

    [Fact]
    public async Task CreateMatch_ComoAdmin_DevuelveCreado()
    {
        var tournamentId = SeedTournament();
        var pair1Id = SeedPair(tournamentId, "Juan", "Pedro");
        var pair2Id = SeedPair(tournamentId, "Ana", "Sol");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches", new
        {
            round = "Cuartos",
            pair1Id,
            pair2Id,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cuartos", body.GetProperty("round").GetString());
        Assert.Equal(pair1Id, body.GetProperty("pair1Id").GetGuid());
        Assert.Equal(pair2Id, body.GetProperty("pair2Id").GetGuid());
    }

    [Fact]
    public async Task CreateMatch_ConRondaVacia_Devuelve400()
    {
        var tournamentId = SeedTournament();
        var pair1Id = SeedPair(tournamentId, "Juan", "Pedro");
        var pair2Id = SeedPair(tournamentId, "Ana", "Sol");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches", new
        {
            round = "",
            pair1Id,
            pair2Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task CreateMatch_ConLasDosParejasIguales_Devuelve400()
    {
        var tournamentId = SeedTournament();
        var pairId = SeedPair(tournamentId, "Juan", "Pedro");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches", new
        {
            round = "Cuartos",
            pair1Id = pairId,
            pair2Id = pairId,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task CreateMatch_ConParejaDeOtroTorneo_Devuelve400()
    {
        var tournamentId = SeedTournament();
        var otherTournamentId = SeedTournament(name: "Otro Torneo", date: "2026-04-01");
        var pair1Id = SeedPair(tournamentId, "Juan", "Pedro");
        var pair2Id = SeedPair(otherTournamentId, "Ana", "Sol");
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches", new
        {
            round = "Cuartos",
            pair1Id,
            pair2Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task UpdateResult_ComoAdmin_CargaElResultado()
    {
        var tournamentId = SeedTournament();
        var pair1Id = SeedPair(tournamentId, "Juan", "Pedro");
        var pair2Id = SeedPair(tournamentId, "Ana", "Sol");
        var matchId = SeedMatch(tournamentId, pair1Id, pair2Id);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/tournaments/matches/{matchId}/result", new
        {
            score = "6-3 6-4",
            winnerPairId = pair1Id,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("6-3 6-4", body.GetProperty("score").GetString());
        Assert.Equal(pair1Id, body.GetProperty("winnerPairId").GetGuid());
    }

    [Fact]
    public async Task UpdateResult_ConGanadorQueNoEsNingunaDeLasDosParejas_Devuelve400()
    {
        var tournamentId = SeedTournament();
        var pair1Id = SeedPair(tournamentId, "Juan", "Pedro");
        var pair2Id = SeedPair(tournamentId, "Ana", "Sol");
        var matchId = SeedMatch(tournamentId, pair1Id, pair2Id);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/tournaments/matches/{matchId}/result", new
        {
            score = "6-3 6-4",
            winnerPairId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task UpdateResult_ConPartidoInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/tournaments/matches/{Guid.NewGuid()}/result", new
        {
            score = "6-3 6-4",
            winnerPairId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }
}
