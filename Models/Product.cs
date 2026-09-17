namespace Padel.Api.Models;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>"alquiler" | "venta"</summary>
    public string Type { get; set; } = "venta";

    public int Stock { get; set; }
    public int MinStock { get; set; }
    public decimal Price { get; set; }
}
