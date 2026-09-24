namespace Nebula.Domain;

public class Product
{
    public string Id { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProductCategory Category { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public string ImageUrl { get; set; } = "";
    public int Stock { get; set; }
    public int Sold { get; set; }
    public double Rating { get; set; }
    public List<ProductVariant> Variants { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public bool Active { get; set; }
}
