using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Padel.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Padel.Api.Tests;

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

    private async Task<Guid> CreateTournamentAsync(HttpClient adminClient)
    {
        var response = await adminClient.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Torneo de test {Guid.NewGuid()}",
            date = "2026-10-01",
            registrationFee = 5000m,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Torneo prohibido",
            date = "2026-10-01",
            registrationFee = 5000m,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ComoAdmin_Devuelve201YLoLista()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");

        var tournamentId = await CreateTournamentAsync(admin);

        var listResponse = await admin.GetAsync("/api/tournaments");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(list.EnumerateArray(), t => t.GetProperty("id").GetGuid() == tournamentId);
    }

    [Fact]
    public async Task Update_ConDatosValidos_Devuelve200YActualiza()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");
        var tournamentId = await CreateTournamentAsync(admin);

        var response = await admin.PutAsJsonAsync($"/api/tournaments/{tournamentId}", new
        {
            name = "Torneo actualizado",
            date = "2026-11-15",
            registrationFee = 7500m,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tournamentId, body.GetProperty("id").GetGuid());
        Assert.Equal("Torneo actualizado", body.GetProperty("name").GetString());
        Assert.Equal("2026-11-15", body.GetProperty("date").GetString());
        Assert.Equal(7500m, body.GetProperty("registrationFee").GetDecimal());
    }

    [Fact]
    public async Task Update_ConIdInexistente_Devuelve404()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await admin.PutAsJsonAsync($"/api/tournaments/{Guid.NewGuid()}", new
        {
            name = "No existe",
            date = "2026-11-15",
            registrationFee = 1000m,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Delete_ComoAdmin_Devuelve204YLoSacaDeLaLista()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");
        var tournamentId = await CreateTournamentAsync(admin);

        var deleteResponse = await admin.DeleteAsync($"/api/tournaments/{tournamentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await admin.GetAsync("/api/tournaments");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(list.EnumerateArray(), t => t.GetProperty("id").GetGuid() == tournamentId);
    }

    [Fact]
    public async Task Delete_ConIdInexistente_Devuelve404()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await admin.DeleteAsync($"/api/tournaments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task CreatePair_ConPaidTrueSinPaymentMethod_Devuelve400()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");
        var tournamentId = await CreateTournamentAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/pairs", new
        {
            player1 = "Juan",
            player2 = "Pedro",
            paid = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task CreatePair_ConDatosValidos_Devuelve201()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");
        var tournamentId = await CreateTournamentAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/pairs", new
        {
            player1 = "Juan",
            player2 = "Pedro",
            paid = true,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tournamentId, body.GetProperty("tournamentId").GetGuid());
        Assert.Equal("efectivo", body.GetProperty("paymentMethod").GetString());
    }

    [Fact]
    public async Task DeletePair_LaSacaDeLaLista()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");
        var tournamentId = await CreateTournamentAsync(admin);

        var createResponse = await admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/pairs", new
        {
            player1 = "Ana",
            player2 = "Lucía",
            paid = false,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var pairId = created.GetProperty("id").GetGuid();

        var deleteResponse = await admin.DeleteAsync($"/api/tournaments/pairs/{pairId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await admin.GetAsync($"/api/tournaments/{tournamentId}/pairs");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(list.EnumerateArray(), p => p.GetProperty("id").GetGuid() == pairId);
    }

    private async Task<(Guid pair1Id, Guid pair2Id)> SeedTwoPairsAsync(HttpClient admin, Guid tournamentId)
    {
        var r1 = await admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/pairs", new
        {
            player1 = "Jugador A1",
            player2 = "Jugador A2",
            paid = false,
        });
        var p1 = await r1.Content.ReadFromJsonAsync<JsonElement>();

        var r2 = await admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/pairs", new
        {
            player1 = "Jugador B1",
            player2 = "Jugador B2",
            paid = false,
        });
        var p2 = await r2.Content.ReadFromJsonAsync<JsonElement>();

        return (p1.GetProperty("id").GetGuid(), p2.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CreateMatch_ConParejasIguales_Devuelve400()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");
        var tournamentId = await CreateTournamentAsync(admin);
        var (pair1Id, _) = await SeedTwoPairsAsync(admin, tournamentId);

        var response = await admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches", new
        {
            round = "Semifinal",
            pair1Id,
            pair2Id = pair1Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMatch_YCargarResultado_ActualizaElPartido()
    {
        var admin = await AuthenticatedClientAsync("admin", "Admin123!");
        var tournamentId = await CreateTournamentAsync(admin);
        var (pair1Id, pair2Id) = await SeedTwoPairsAsync(admin, tournamentId);

        var matchResponse = await admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches", new
        {
            round = "Final",
            pair1Id,
            pair2Id,
        });
        Assert.Equal(HttpStatusCode.Created, matchResponse.StatusCode);
        var match = await matchResponse.Content.ReadFromJsonAsync<JsonElement>();
        var matchId = match.GetProperty("id").GetGuid();

        var badResult = await admin.PutAsJsonAsync($"/api/tournaments/matches/{matchId}/result", new
        {
            score = "6-4 6-3",
            winnerPairId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, badResult.StatusCode);

        var resultResponse = await admin.PutAsJsonAsync($"/api/tournaments/matches/{matchId}/result", new
        {
            score = "6-4 6-3",
            winnerPairId = pair1Id,
        });
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        var body = await resultResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("6-4 6-3", body.GetProperty("score").GetString());
        Assert.Equal(pair1Id, body.GetProperty("winnerPairId").GetGuid());
    }
}
