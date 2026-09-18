using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;
using Padel.Api.Models;

namespace Padel.Api.Controllers;

// Productos (paletas para alquilar, pelotas/agua/gaseosa para vender). Lectura
// para ambos roles; alta/edición/baja de producto solo admin. Vender sí pueden
// los dos, por eso Sell() no tiene el [Authorize(Roles = "admin")] de arriba.
[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private static readonly string[] ValidPaymentMethods = { "efectivo", "transferencia", "tarjeta" };

    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var products = await _db.Products.OrderBy(p => p.Name).ToListAsync();
        return Ok(products.Select(ToDto));
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] ProductDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { error });

        var product = new Product
        {
            Name = dto.Name!.Trim(),
            Type = dto.Type!,
            Stock = dto.Stock,
            MinStock = dto.MinStock,
            Price = dto.Price,
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, ToDto(product));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProductDto dto)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(new { error = "Producto no encontrado" });

        var error = Validate(dto);
        if (error != null) return BadRequest(new { error });

        product.Name = dto.Name!.Trim();
        product.Type = dto.Type!;
        product.Stock = dto.Stock;
        product.MinStock = dto.MinStock;
        product.Price = dto.Price;
        await _db.SaveChangesAsync();

        return Ok(ToDto(product));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(new { error = "Producto no encontrado" });

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // Rutas absolutas /api/product-sales (no /api/products/...): así lo pide
    // el contrato, es un recurso propio aunque viva en este mismo controller.
    [HttpGet("/api/product-sales")]
    public async Task<IActionResult> GetSales([FromQuery] string? date)
    {
        var query = _db.ProductSales.Include(s => s.Product).AsQueryable();

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParse(date, out var day))
                return BadRequest(new { error = "Fecha inválida, formato esperado YYYY-MM-DD" });

            query = query.Where(s => s.CreatedAt.Year == day.Year && s.CreatedAt.Month == day.Month && s.CreatedAt.Day == day.Day);
        }

        var sales = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
        return Ok(sales.Select(ToSaleDto));
    }

    [HttpPost("/api/product-sales")]
    public async Task<IActionResult> Sell([FromBody] ProductSaleDto dto)
    {
        if (dto.Quantity <= 0)
            return BadRequest(new { error = "La cantidad debe ser mayor a 0" });

        if (!ValidPaymentMethods.Contains(dto.PaymentMethod))
            return BadRequest(new { error = "paymentMethod debe ser 'efectivo', 'transferencia' o 'tarjeta'" });

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId);
        if (product == null) return NotFound(new { error = "Producto no encontrado" });

        if (product.Stock < dto.Quantity)
            return BadRequest(new { error = "No hay stock suficiente para esta venta" });

        Booking? booking = null;
        if (dto.BookingId.HasValue)
        {
            booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == dto.BookingId.Value);
            if (booking == null) return NotFound(new { error = "Reserva no encontrada" });
        }

        var amount = product.Price * dto.Quantity;
        product.Stock -= dto.Quantity;

        var sale = new ProductSale
        {
            ProductId = product.Id,
            Quantity = dto.Quantity,
            Amount = amount,
            BookingId = dto.BookingId,
            PaymentMethod = dto.PaymentMethod,
        };
        _db.ProductSales.Add(sale);

        if (booking != null)
        {
            booking.TotalAmount += amount;
        }

        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, new
        {
            id = sale.Id,
            productId = sale.ProductId,
            quantity = sale.Quantity,
            amount = sale.Amount,
            bookingId = sale.BookingId,
            paymentMethod = sale.PaymentMethod,
            createdAt = sale.CreatedAt,
        });
    }

    // Corrige una venta cargada por error (cantidad o medio de pago). Repone
    // el stock de la cantidad vieja y descuenta la nueva, y si la venta está
    // sumada a una reserva reajusta su totalAmount por la diferencia.
    [HttpPut("/api/product-sales/{id:guid}")]
    public async Task<IActionResult> UpdateSale(Guid id, [FromBody] ProductSaleUpdateDto dto)
    {
        var sale = await _db.ProductSales.Include(s => s.Product).FirstOrDefaultAsync(s => s.Id == id);
        if (sale == null) return NotFound(new { error = "Venta no encontrada" });

        if (dto.Quantity <= 0)
            return BadRequest(new { error = "La cantidad debe ser mayor a 0" });

        if (!ValidPaymentMethods.Contains(dto.PaymentMethod))
            return BadRequest(new { error = "paymentMethod debe ser 'efectivo', 'transferencia' o 'tarjeta'" });

        var product = sale.Product ?? await _db.Products.FirstOrDefaultAsync(p => p.Id == sale.ProductId);
        if (product == null) return NotFound(new { error = "Producto no encontrado" });

        var stockDisponible = product.Stock + sale.Quantity;
        if (stockDisponible < dto.Quantity)
            return BadRequest(new { error = "No hay stock suficiente para esta venta" });

        Booking? booking = null;
        if (sale.BookingId.HasValue)
        {
            booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == sale.BookingId.Value);
        }

        var nuevoAmount = product.Price * dto.Quantity;
        product.Stock = stockDisponible - dto.Quantity;

        if (booking != null)
        {
            booking.TotalAmount = booking.TotalAmount - sale.Amount + nuevoAmount;
        }

        sale.Quantity = dto.Quantity;
        sale.Amount = nuevoAmount;
        sale.PaymentMethod = dto.PaymentMethod;

        await _db.SaveChangesAsync();

        return Ok(ToSaleDto(sale));
    }

    // Borra una venta cargada por error: repone el stock y, si estaba sumada
    // a una reserva, le resta el monto del totalAmount.
    [HttpDelete("/api/product-sales/{id:guid}")]
    public async Task<IActionResult> DeleteSale(Guid id)
    {
        var sale = await _db.ProductSales.FirstOrDefaultAsync(s => s.Id == id);
        if (sale == null) return NotFound(new { error = "Venta no encontrada" });

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == sale.ProductId);
        if (product != null)
        {
            product.Stock += sale.Quantity;
        }

        if (sale.BookingId.HasValue)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == sale.BookingId.Value);
            if (booking != null)
            {
                booking.TotalAmount -= sale.Amount;
            }
        }

        _db.ProductSales.Remove(sale);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static object ToSaleDto(ProductSale s) => new
    {
        id = s.Id,
        productId = s.ProductId,
        productName = s.Product?.Name,
        quantity = s.Quantity,
        amount = s.Amount,
        bookingId = s.BookingId,
        paymentMethod = s.PaymentMethod,
        createdAt = s.CreatedAt,
    };

    private static object ToDto(Product p) => new
    {
        id = p.Id,
        name = p.Name,
        type = p.Type,
        stock = p.Stock,
        minStock = p.MinStock,
        price = p.Price,
        lowStock = p.Stock <= p.MinStock,
    };

    private static string? Validate(ProductDto? dto)
    {
        if (dto == null) return "Datos inválidos";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "El nombre es requerido";
        if (dto.Type != "alquiler" && dto.Type != "venta")
            return "type debe ser 'alquiler' o 'venta'";
        if (dto.Stock < 0) return "El stock no puede ser negativo";
        if (dto.MinStock < 0) return "El stock mínimo no puede ser negativo";
        if (dto.Price <= 0) return "El precio debe ser mayor a 0";
        return null;
    }
}

public class ProductDto
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public int Stock { get; set; }
    public int MinStock { get; set; }
    public decimal Price { get; set; }
}

public class ProductSaleDto
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public Guid? BookingId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}

public class ProductSaleUpdateDto
{
    public int Quantity { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}
