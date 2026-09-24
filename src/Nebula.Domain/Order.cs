namespace Nebula.Domain;

public class Order
{
    /// <summary>Format: <c>ord_000123</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-facing order number, starting at 1001.</summary>
    public int Number { get; set; }

    public string CustomerId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string CustomerAvatarUrl { get; set; } = "";
    public List<OrderItem> Items { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public DateTime CreatedAt { get; set; }
    public Address ShippingAddress { get; set; } = new();
    public List<StatusChange> History { get; set; } = [];
}
