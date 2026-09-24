namespace Nebula.Domain;

public class Customer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int OrdersCount { get; set; }
    public decimal LifetimeValue { get; set; }
    public DateTime? LastOrderAt { get; set; }
    public string Notes { get; set; } = "";
}
