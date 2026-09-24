using System.Text.Json.Serialization;

namespace Nebula.Domain;

public class StatusChange
{
    public OrderStatus Status { get; set; }
    public DateTime At { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }
}
