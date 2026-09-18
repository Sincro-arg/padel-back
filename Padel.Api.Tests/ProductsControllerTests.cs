using System.Linq;
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

    private (Guid Id, decimal TotalAmount) SeedBooking(decimal totalAmount = 8000m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var court = new Padel.Api.Models.Court { Name = $"Cancha de test {Guid.NewGuid()}" };
        db.Courts.Add(court);

        var booking = new Padel.Api.Models.Booking
        {
            CourtId = court.Id,
            CustomerName = "Cliente de test",
            CustomerPhone = "1122334455",
            Date = DateOnly.FromDateTime(DateTime.Now).AddDays(1),
            StartHour = 10,
            EndHour = 11,
            TotalAmount = totalAmount,
        };
        db.Bookings.Add(booking);
        db.SaveChanges();

        return (booking.Id, booking.TotalAmount);
    }

    [Fact]
    public async Task GetAll_ComoEmpleado_Devuelve200ConLista()
    {
        SeedProduct("Producto de test GET", stock: 5);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetAll_SinToken_Devuelve401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ProductoConStockBajo_DevuelveLowStockTrue()
    {
        SeedProduct("Producto con poco stock", stock: 1, minStock: 3);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var found = body.EnumerateArray().First(p => p.GetProperty("name").GetString() == "Producto con poco stock");
        Assert.True(found.GetProperty("lowStock").GetBoolean());
    }

    [Fact]
    public async Task Create_ComoAdmin_DevuelveCreado()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Paleta nueva",
            type = "alquiler",
            stock = 10,
            minStock = 2,
            price = 500m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paleta nueva", body.GetProperty("name").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Create_ComoEmpleado_Devuelve403()
    {
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Producto de empleado",
            type = "venta",
            stock = 5,
            minStock = 1,
            price = 100m,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_SinNombre_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "",
            type = "venta",
            stock = 5,
            minStock = 1,
            price = 100m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Create_TypeInvalido_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Producto con type invalido",
            type = "otro",
            stock = 5,
            minStock = 1,
            price = 100m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_PrecioCero_Devuelve400()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Producto sin precio",
            type = "venta",
            stock = 5,
            minStock = 1,
            price = 0m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ComoAdmin_DevuelveActualizado()
    {
        var (id, _) = SeedProduct("Producto para actualizar", stock: 5);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/products/{id}", new
        {
            name = "Producto actualizado",
            type = "venta",
            stock = 8,
            minStock = 2,
            price = 200m,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Producto actualizado", body.GetProperty("name").GetString());
        Assert.Equal(8, body.GetProperty("stock").GetInt32());
    }

    [Fact]
    public async Task Update_ComoEmpleado_Devuelve403()
    {
        var (id, _) = SeedProduct("Producto que empleado no puede tocar", stock: 5);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PutAsJsonAsync($"/api/products/{id}", new
        {
            name = "Intento de empleado",
            type = "venta",
            stock = 5,
            minStock = 1,
            price = 100m,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_IdInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", new
        {
            name = "No existe",
            type = "venta",
            stock = 5,
            minStock = 1,
            price = 100m,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_SinNombre_Devuelve400()
    {
        var (id, _) = SeedProduct("Producto para update sin nombre", stock: 5);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.PutAsJsonAsync($"/api/products/{id}", new
        {
            name = "",
            type = "venta",
            stock = 5,
            minStock = 1,
            price = 100m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ComoAdmin_Devuelve204YLoSaca()
    {
        var (id, _) = SeedProduct("Producto para borrar", stock: 5);
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/products/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Products.AnyAsync(p => p.Id == id));
    }

    [Fact]
    public async Task Delete_ComoEmpleado_Devuelve403()
    {
        var (id, _) = SeedProduct("Producto que empleado no puede borrar", stock: 5);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.DeleteAsync($"/api/products/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_IdInexistente_Devuelve404()
    {
        var client = await AuthenticatedClientAsync("admin", "Admin123!");

        var response = await client.DeleteAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    [Fact]
    public async Task Sell_ConBookingId_SumaAmountAlTotalAmountDeLaReserva()
    {
        var (productId, _) = SeedProduct("Pelota de test con reserva", stock: 10, price: 500m);
        var (bookingId, totalAmountOriginal) = SeedBooking(totalAmount: 8000m);
        var client = await AuthenticatedClientAsync("empleado", "Empleado123!");

        var response = await client.PostAsJsonAsync("/api/product-sales", new
        {
            productId,
            quantity = 2,
            bookingId,
            paymentMethod = "efectivo",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1000m, body.GetProperty("amount").GetDecimal());
        Assert.Equal(bookingId, body.GetProperty("bookingId").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var booking = await db.Bookings.FirstAsync(b => b.Id == bookingId);
        Assert.Equal(totalAmountOriginal + 1000m, booking.TotalAmount);
    }
}
