using Nebula.Domain;

namespace Nebula.Infrastructure.Seeding;

/// <summary>Static seed data copied verbatim from <c>nebula-commerce/src/app/mock-api/seed.ts</c>.</summary>
public static class SeedCatalog
{
    public const int ProductCount = 60;
    public const int CustomerCount = 700;
    public const int OrderCount = 4800;

    /// <summary>History window (~13 months).</summary>
    public const int HistoryDays = 395;

    public sealed record CategorySpec(
        ProductCategory Category,
        string Code,
        IReadOnlyList<string> Nouns,
        double MinPrice,
        double MaxPrice,
        IReadOnlyList<string> Sizes,
        IReadOnlyList<string> Colors,
        IReadOnlyList<string> Blurbs);

    /// <param name="Code">ISO country code.</param>
    /// <param name="Postal">Postal code pattern: <c>#</c> digit, <c>?</c> letter.</param>
    /// <param name="Name">Country name.</param>
    /// <param name="Weight">Relative share of customers.</param>
    /// <param name="Dial">Phone dial prefix.</param>
    /// <param name="Cities">Cities used for shipping addresses.</param>
    public sealed record CountrySpec(
        string Code,
        string Postal,
        string Name,
        double Weight,
        string Dial,
        IReadOnlyList<string> Cities);

    public static readonly IReadOnlyList<string> Brands =
    [
        "Aurora", "Orbit", "Nova", "Lumen", "Zenith", "Stellar", "Comet", "Halo",
        "Eclipse", "Vega", "Solstice", "Drift", "Atlas", "Cascade", "Ember", "Polaris",
    ];

    /// <summary>Categories in the same order as the TS object keys.</summary>
    public static readonly IReadOnlyList<CategorySpec> Categories =
    [
        new(
            ProductCategory.Apparel,
            "APP",
            [
                "Linen Shirt", "Merino Sweater", "Denim Jacket", "Organic Cotton Tee", "Slim Chino Pants",
                "Fleece Hoodie", "Puffer Vest", "Oxford Shirt", "Knit Cardigan", "Rain Shell Jacket",
            ],
            29,
            149,
            ["XS", "S", "M", "L", "XL"],
            ["Black", "Navy", "Sand", "Olive", "White", "Charcoal"],
            [
                "Relaxed fit with breathable, garment-washed fabric.",
                "Tailored silhouette made from responsibly sourced fibres.",
                "Everyday essential that softens with every wash.",
            ]),
        new(
            ProductCategory.Footwear,
            "FTW",
            [
                "Runner Sneakers", "Leather Chelsea Boots", "Canvas Low-Tops", "Trail Running Shoes", "Suede Loafers",
                "Recovery Slides", "High-Top Sneakers", "Hiking Boots", "Knit Trainers", "Court Sneakers",
            ],
            59,
            219,
            ["38", "39", "40", "41", "42", "43", "44", "45"],
            ["Black", "White", "Grey", "Tan", "Navy"],
            [
                "Cushioned midsole and a grippy rubber outsole.",
                "Premium upper with a supportive, all-day footbed.",
                "Lightweight build designed for city miles.",
            ]),
        new(
            ProductCategory.Accessories,
            "ACC",
            [
                "Leather Wallet", "Canvas Backpack", "Wool Beanie", "Polarized Sunglasses", "Crossbody Bag",
                "Silk Scarf", "Minimalist Watch", "Leather Belt", "Weekender Duffel", "Card Holder",
            ],
            19,
            189,
            ["One Size"],
            ["Black", "Cognac", "Forest", "Stone", "Burgundy"],
            [
                "Crafted from full-grain materials built to age well.",
                "Clean lines and thoughtful pockets for daily carry.",
                "Finished with brushed metal hardware.",
            ]),
        new(
            ProductCategory.Electronics,
            "ELC",
            [
                "Wireless Earbuds", "Smart Watch", "Bluetooth Speaker", "Noise-Cancelling Headphones", "Power Bank 20000",
                "Mechanical Keyboard", "Wireless Charger", "Action Camera", "Smart Desk Lamp", "Fitness Tracker",
            ],
            29,
            399,
            ["Standard"],
            ["Midnight", "Silver", "Space Grey", "Arctic White", "Cobalt"],
            [
                "All-day battery life with fast USB-C charging.",
                "Seamless pairing and a companion app for fine-tuning.",
                "Precision-engineered with premium, low-latency components.",
            ]),
        new(
            ProductCategory.Home,
            "HOM",
            [
                "Ceramic Vase", "Soy Scented Candle", "Linen Throw Blanket", "Oak Desk Organizer", "Pour-Over Coffee Set",
                "Wool Area Rug", "Stoneware Mug Set", "Cotton Towel Set", "Glass Carafe", "Walnut Serving Board",
            ],
            15,
            179,
            ["Small", "Medium", "Large"],
            ["Ivory", "Terracotta", "Sage", "Slate", "Oat"],
            [
                "Handmade in small batches with natural materials.",
                "A calm, minimal piece that works in any room.",
                "Designed to be used every day and loved for years.",
            ]),
        new(
            ProductCategory.Beauty,
            "BTY",
            [
                "Hydrating Serum", "Daily Face Cream", "Lip Balm Trio", "Eau de Parfum", "Clay Detox Mask",
                "Body Lotion", "Vitamin C Serum", "Nourishing Hair Oil", "Gentle Cleanser", "SPF 50 Sunscreen",
            ],
            12,
            89,
            ["30 ml", "50 ml", "100 ml"],
            ["Unscented", "Citrus", "Lavender", "Rose"],
            [
                "Dermatologist-tested, vegan and cruelty-free formula.",
                "Lightweight texture that absorbs in seconds.",
                "Packed with botanicals for a healthy, lasting glow.",
            ]),
    ];

    public static readonly IReadOnlyList<CountrySpec> Countries =
    [
        new("US", "#####", "United States", 30, "+1", ["New York", "Austin", "Seattle", "Chicago", "San Diego", "Denver"]),
        new("GB", "??# #??", "United Kingdom", 12, "+44", ["London", "Manchester", "Bristol", "Edinburgh", "Leeds"]),
        new("DE", "#####", "Germany", 10, "+49", ["Berlin", "Munich", "Hamburg", "Cologne", "Leipzig"]),
        new("FR", "#####", "France", 7, "+33", ["Paris", "Lyon", "Marseille", "Bordeaux", "Nantes"]),
        new("CA", "?#? #?#", "Canada", 7, "+1", ["Toronto", "Vancouver", "Montreal", "Calgary"]),
        new("AU", "####", "Australia", 5, "+61", ["Sydney", "Melbourne", "Brisbane", "Perth"]),
        new("NL", "#### ??", "Netherlands", 4, "+31", ["Amsterdam", "Rotterdam", "Utrecht", "Eindhoven"]),
        new("PL", "##-###", "Poland", 4, "+48", ["Warsaw", "Krakow", "Wroclaw", "Gdansk"]),
        new("ES", "#####", "Spain", 4, "+34", ["Madrid", "Barcelona", "Valencia", "Seville"]),
        new("IT", "#####", "Italy", 4, "+39", ["Milan", "Rome", "Turin", "Florence"]),
        new("SE", "### ##", "Sweden", 3, "+46", ["Stockholm", "Gothenburg", "Malmo"]),
        new("JP", "###-####", "Japan", 4, "+81", ["Tokyo", "Osaka", "Kyoto", "Fukuoka"]),
        new("BR", "#####-###", "Brazil", 3, "+55", ["Sao Paulo", "Rio de Janeiro", "Curitiba"]),
        new("UA", "#####", "Ukraine", 3, "+380", ["Kyiv", "Lviv", "Odesa", "Kharkiv"]),
    ];

    public static readonly IReadOnlyList<string> CustomerNotes =
    [
        "VIP customer, prefers express shipping.",
        "Asked to combine shipments when possible.",
        "Gift orders, no prices on packing slip.",
        "Reached out about sizing, recommend one size up.",
        "Wholesale inquiry pending.",
    ];

    public static readonly IReadOnlyList<string> Carriers = ["DHL Express", "UPS", "FedEx", "USPS", "Royal Mail", "DPD"];

    public static readonly IReadOnlyList<string> CancellationNotes =
    [
        "Customer requested cancellation",
        "Payment declined",
        "Item out of stock",
    ];

    /// <summary>Hand-picked Lorem Picsum photo ids per product noun (no two products share a photo).</summary>
    public static readonly IReadOnlyDictionary<string, int> ProductPhotos = new Dictionary<string, int>
    {
        ["Linen Shirt"] = 325,
        ["Merino Sweater"] = 755,
        ["Denim Jacket"] = 1059,
        ["Organic Cotton Tee"] = 535,
        ["Slim Chino Pants"] = 604,
        ["Fleece Hoodie"] = 375,
        ["Puffer Vest"] = 669,
        ["Oxford Shirt"] = 856,
        ["Knit Cardigan"] = 758,
        ["Rain Shell Jacket"] = 338,
        ["Runner Sneakers"] = 817,
        ["Leather Chelsea Boots"] = 858,
        ["Canvas Low-Tops"] = 103,
        ["Trail Running Shoes"] = 177,
        ["Suede Loafers"] = 21,
        ["Recovery Slides"] = 156,
        ["High-Top Sneakers"] = 662,
        ["Hiking Boots"] = 455,
        ["Knit Trainers"] = 22,
        ["Court Sneakers"] = 1001,
        ["Leather Wallet"] = 464,
        ["Canvas Backpack"] = 342,
        ["Wool Beanie"] = 823,
        ["Polarized Sunglasses"] = 64,
        ["Crossbody Bag"] = 7,
        ["Silk Scarf"] = 1005,
        ["Minimalist Watch"] = 26,
        ["Leather Belt"] = 491,
        ["Weekender Duffel"] = 998,
        ["Card Holder"] = 36,
        ["Wireless Earbuds"] = 160,
        ["Smart Watch"] = 175,
        ["Bluetooth Speaker"] = 529,
        ["Noise-Cancelling Headphones"] = 39,
        ["Power Bank 20000"] = 816,
        ["Mechanical Keyboard"] = 366,
        ["Wireless Charger"] = 504,
        ["Action Camera"] = 250,
        ["Smart Desk Lamp"] = 445,
        ["Fitness Tracker"] = 367,
        ["Ceramic Vase"] = 1068,
        ["Soy Scented Candle"] = 999,
        ["Linen Throw Blanket"] = 1062,
        ["Oak Desk Organizer"] = 20,
        ["Pour-Over Coffee Set"] = 1060,
        ["Wool Area Rug"] = 625,
        ["Stoneware Mug Set"] = 635,
        ["Cotton Towel Set"] = 691,
        ["Glass Carafe"] = 225,
        ["Walnut Serving Board"] = 292,
        ["Hydrating Serum"] = 159,
        ["Daily Face Cream"] = 493,
        ["Lip Balm Trio"] = 429,
        ["Eau de Parfum"] = 360,
        ["Clay Detox Mask"] = 1027,
        ["Body Lotion"] = 365,
        ["Vitamin C Serum"] = 517,
        ["Nourishing Hair Oil"] = 312,
        ["Gentle Cleanser"] = 306,
        ["SPF 50 Sunscreen"] = 643,
    };

    /// <summary>Relative order likelihood per UTC hour: quiet nights, evening peak 18-22h.</summary>
    public static readonly IReadOnlyList<double> HourWeights =
    [
        0.3, 0.2, 0.15, 0.1, 0.1, 0.15, 0.3, 0.5, 0.8, 1, 1.1, 1.2, 1.3, 1.2, 1.1, 1.1, 1.2, 1.4, 2.2,
        2.6, 2.8, 2.6, 2.1, 1,
    ];

    /// <summary>
    /// Final-status odds by order age, sized for ~15 orders/day; anything older than the last band is delivered.
    /// </summary>
    public static readonly IReadOnlyList<StatusBand> StatusByAge =
    [
        new(0.5, [(OrderStatus.New, 0.7), (OrderStatus.Packing, 0.3)]),
        new(1, [(OrderStatus.New, 0.3), (OrderStatus.Packing, 0.55), (OrderStatus.Shipped, 0.15)]),
        new(1.5, [(OrderStatus.New, 0.2), (OrderStatus.Packing, 0.4), (OrderStatus.Shipped, 0.4)]),
        new(2, [(OrderStatus.New, 0.05), (OrderStatus.Packing, 0.1), (OrderStatus.Shipped, 0.85)]),
        new(3, [(OrderStatus.Shipped, 0.5), (OrderStatus.Delivered, 0.5)]),
        new(4, [(OrderStatus.Shipped, 0.15), (OrderStatus.Delivered, 0.85)]),
    ];

    public sealed record StatusBand(double MaxAgeDays, IReadOnlyList<(OrderStatus Status, double Weight)> Odds);

    public static User DefaultUser() => new()
    {
        Id = "usr_1",
        Name = "Alex Morgan",
        Email = "alex@nebula.store",
        AvatarUrl = "https://i.pravatar.cc/80?u=alex",
        Role = "Admin",
    };

    public static string ProductImageUrl(string noun, int n) =>
        ProductPhotos.TryGetValue(noun, out var photo)
            ? $"https://picsum.photos/id/{photo}/400/400"
            : $"https://picsum.photos/seed/nebula-{n}/400/400";
}
