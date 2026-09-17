using System.Net;
using System.Net.Http.Json;

namespace Padel.Api.Tests;

public class AuthControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly HttpClient _client;

    public AuthControllerTests(PadelApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_DevuelveTokenYUsuario()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal("admin", body.User.Role);
        Assert.False(string.IsNullOrWhiteSpace(body.User.Id));
    }

    [Fact]
    public async Task Login_ConCredencialesInvalidas_Devuelve401ConError()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "contraseña-incorrecta" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Error));
    }

    private class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserResponse User { get; set; } = new();
    }

    private class UserResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    private class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }
}
