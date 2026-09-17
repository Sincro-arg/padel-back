namespace Padel.Api.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"admin" | "empleado"</summary>
    public string Role { get; set; } = "empleado";
}
