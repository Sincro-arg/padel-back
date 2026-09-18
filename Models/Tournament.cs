namespace Padel.Api.Models;

public class Tournament
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public decimal RegistrationFee { get; set; }
}
