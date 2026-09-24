using System.Text.Json.Serialization;

namespace Nebula.Domain;

public enum OrderStatus
{
    [JsonStringEnumMemberName("new")] New,
    [JsonStringEnumMemberName("packing")] Packing,
    [JsonStringEnumMemberName("shipped")] Shipped,
    [JsonStringEnumMemberName("delivered")] Delivered,
    [JsonStringEnumMemberName("cancelled")] Cancelled,
}

public enum PaymentMethod
{
    [JsonStringEnumMemberName("card")] Card,
    [JsonStringEnumMemberName("paypal")] PayPal,
    [JsonStringEnumMemberName("apple_pay")] ApplePay,
}

public enum ProductCategory
{
    Apparel,
    Footwear,
    Accessories,
    Electronics,
    Home,
    Beauty,
}
