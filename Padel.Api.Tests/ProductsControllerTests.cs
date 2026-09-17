using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Padel.Api.Data;

namespace Padel.Api.Tests;

public class ProductsControllerTests : IClassFixture<PadelApiFactory>
{
    private readonly PadelApiFactory _factory;

    public ProductsControllerTests(PadelApiFactory factory)
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

    private (Guid Id, int Stock) SeedProduct(string name, int stock, int minStock = 1, decimal price = 1000m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Padel.Api.Models.Product
        {
            Name = name,
            Type = "venta",
            Stock = stock,
            MinStock = minStock,
            Price = price,
        };
        db.Products.Add(product);
        db.SaveChanges();

        return (product.Id, product.Stock);
    }

    [Fact]
    public async Task Sell_ConStockSuficiente_DescuentaStock()
    {
        var (productId, _) = SeedProduct("Pelota de test", stock: 10);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/product-sales", new
        {
            productId,
            quantity = 3,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.FirstAsync(p => p.Id == productId);
        Assert.Equal(7, product.Stock);
    }

    [Fact]
    public async Task Sell_SinStockSuficiente_Devuelve400YNoDescuenta()
    {
        var (productId, _) = SeedProduct("Agua de test", stock: 2);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/product-sales", new
        {
            productId,
            quantity = 5,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.FirstAsync(p => p.Id == productId);
        Assert.Equal(2, product.Stock);
    }
}
