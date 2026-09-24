namespace Nebula.Domain;

public class StatusChange
{
    public OrderStatus Status { get; set; }
    public DateTime At { get; set; }
    public string? Note { get; set; }
}
