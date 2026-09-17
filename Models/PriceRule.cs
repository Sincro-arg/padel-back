namespace Padel.Api.Models;

public class PriceRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>"weekday" | "weekend"</summary>
    public string DayType { get; set; } = "weekday";

    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public decimal PricePerHour { get; set; }
}
