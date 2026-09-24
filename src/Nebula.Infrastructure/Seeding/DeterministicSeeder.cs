using Bogus;
using Nebula.Application.Common;
using Nebula.Domain;

namespace Nebula.Infrastructure.Seeding;

public sealed record SeedData(
    IReadOnlyList<Product> Products,
    IReadOnlyList<Customer> Customers,
    IReadOnlyList<Order> Orders,
    User User);

/// <summary>
/// C# port of <c>nebula-commerce/src/app/mock-api/seed.ts</c>. Pure: the same anchor always yields
/// identical data. Values are not expected to match faker-js output, only the shapes and rules.
/// </summary>
public static class DeterministicSeeder
{
    public const int RandomSeed = 42;

    private const long HourMs = 3_600_000;
    private const long DayMs = 24 * HourMs;

    private sealed record CustomerSeed(Customer Customer, Address Address, double Weight);

    private sealed record Step(OrderStatus Status, long DelayMs, string? Note = null);

    public static SeedData Create(DateTime anchorUtc)
    {
        if (anchorUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Anchor must be a UTC time.", nameof(anchorUtc));
        }

        // A private Randomizer keeps the sequence independent of Bogus' global static seed.
        var f = new Faker("en") { Random = new Randomizer(RandomSeed) };
        var nowMs = ToMs(anchorUtc);

        var products = CreateProducts(f, nowMs);
        var customerSeeds = CreateCustomers(f, nowMs);
        var orders = CreateOrders(f, nowMs, products, customerSeeds);

        ApplyProductSales(products, orders);
        var customers = ApplyCustomerAggregates(f, customerSeeds, orders);

        return new SeedData(products, customers, orders, SeedCatalog.DefaultUser());
    }

    private static List<Product> CreateProducts(Faker f, long nowMs)
    {
        var products = new List<Product>(SeedCatalog.ProductCount);
        var perCategory = SeedCatalog.ProductCount / SeedCatalog.Categories.Count;

        foreach (var spec in SeedCatalog.Categories)
        {
            var brands = f.Random.Shuffle(SeedCatalog.Brands).ToList();
            var nouns = f.Random.Shuffle(spec.Nouns).ToList();

            for (var i = 0; i < perCategory; i++)
            {
                var n = products.Count + 1;
                var brand = brands[i];
                var noun = nouns[i % nouns.Count];
                var id = $"prd_{n:D4}";
                var price = RetailPrice(f, f.Random.Double(spec.MinPrice, spec.MaxPrice));
                decimal? compareAtPrice = f.Random.Bool(0.3f)
                    ? RetailPrice(f, (double)price * f.Random.Double(1.15, 1.4))
                    : null;

                var variants = CreateVariants(f, id, spec);

                products.Add(new Product
                {
                    Id = id,
                    Sku = $"{spec.Code}-{brand[..3].ToUpperInvariant()}-{n:D3}",
                    Name = $"{brand} {noun}",
                    Description = $"{Pick(f, spec.Blurbs)} Part of the {brand} collection by Nebula.",
                    Category = spec.Category,
                    Price = price,
                    CompareAtPrice = compareAtPrice,
                    ImageUrl = SeedCatalog.ProductImageUrl(noun, n),
                    Stock = variants.Sum(v => v.Stock),
                    Sold = 0,
                    Rating = f.Random.Int(36, 50) / 10.0,
                    Variants = variants,
                    CreatedAt = FromMs(nowMs - (f.Random.Int(420, 900) * DayMs)),
                    Active = f.Random.Bool(0.92f),
                });
            }
        }

        return products;
    }

    private static List<ProductVariant> CreateVariants(Faker f, string productId, SeedCatalog.CategorySpec spec)
    {
        var sizes = f.Random.ListItems(spec.Sizes.ToList(), f.Random.Int(1, Math.Min(3, spec.Sizes.Count)));
        var colors = f.Random.ListItems(spec.Colors.ToList(), f.Random.Int(1, 2));

        // Stock profile: ~8% sold out, ~15% running low, rest healthy.
        var profile = f.Random.Double(0, 1);
        var maxPerVariant = profile < 0.08 ? 0 : profile < 0.23 ? 2 : 45;
        var minPerVariant = profile < 0.23 ? 0 : 3;

        var variants = new List<ProductVariant>();
        foreach (var size in sizes)
        {
            foreach (var color in colors)
            {
                variants.Add(new ProductVariant
                {
                    Id = $"{productId}_v{variants.Count + 1}",
                    Size = size,
                    Color = color,
                    Stock = f.Random.Int(minPerVariant, maxPerVariant),
                });
            }
        }

        return variants;
    }

    private static List<CustomerSeed> CreateCustomers(Faker f, long nowMs)
    {
        var countryCum = Cumulate(SeedCatalog.Countries.Select(c => c.Weight));
        var seeds = new List<CustomerSeed>(SeedCatalog.CustomerCount);

        for (var i = 1; i <= SeedCatalog.CustomerCount; i++)
        {
            var id = $"cus_{i:D4}";
            var firstName = f.Name.FirstName();
            var lastName = f.Name.LastName();
            var country = SeedCatalog.Countries[PickWeighted(f, countryCum)];
            var address = new Address
            {
                Line1 = f.Address.StreetAddress(),
                City = Pick(f, country.Cities),
                Country = country.Name,
                CountryCode = country.Code,
                PostalCode = f.Random.Replace(country.Postal),
            };

            var customer = new Customer
            {
                Id = id,
                Name = $"{firstName} {lastName}",
                Email = f.Internet.Email(firstName, lastName).ToLowerInvariant(),
                AvatarUrl = $"https://i.pravatar.cc/80?u={id}",
                Phone = $"{country.Dial} {f.Random.Replace("###")} {f.Random.Replace("###")} {f.Random.Replace("####")}",
                Country = country.Name,
                CountryCode = country.Code,
                CreatedAt = FromMs(nowMs - f.Random.Long(5 * DayMs, (SeedCatalog.HistoryDays + 30) * DayMs)),
                OrdersCount = 0,
                LifetimeValue = 0,
                LastOrderAt = null,
                Notes = f.Random.Bool(0.15f) ? Pick(f, SeedCatalog.CustomerNotes) : "",
            };

            // Skewed weights: a few loyal repeat buyers, a long tail of occasional ones.
            seeds.Add(new CustomerSeed(customer, address, Math.Pow(f.Random.Double(0.2, 2), 3)));
        }

        return seeds;
    }

    private static List<Order> CreateOrders(Faker f, long nowMs, List<Product> products, List<CustomerSeed> customers)
    {
        // Day weights: ~+6% per month growth, weekends +20%.
        var dayWeights = new List<double>(SeedCatalog.HistoryDays + 1);
        for (var daysAgo = 0; daysAgo <= SeedCatalog.HistoryDays; daysAgo++)
        {
            var monthsFromStart = (SeedCatalog.HistoryDays - daysAgo) / 30.4;
            var weekday = FromMs(nowMs - (daysAgo * DayMs)).DayOfWeek;
            var weekend = weekday is DayOfWeek.Sunday or DayOfWeek.Saturday ? 1.2 : 1;
            dayWeights.Add(Math.Pow(1.06, monthsFromStart) * weekend);
        }

        var dayCum = Cumulate(dayWeights);
        var hourCum = Cumulate(SeedCatalog.HourWeights);
        var customerCum = Cumulate(customers.Select(c => c.Weight));
        var productCum = Cumulate(products.Select(p => (p.Active ? 1 : 0.2) * Math.Pow(f.Random.Double(0.3, 3), 2)).ToList());
        var startOfToday = nowMs / DayMs * DayMs;

        // Jittered systematic sampling over the day weights: every order still lands on a random
        // day, but daily volumes follow the growth/weekend curve closely instead of drifting with
        // sampling noise, so period-over-period comparisons reflect the intended trend.
        var totalDayWeight = dayCum[^1];
        var timestamps = new List<long>(SeedCatalog.OrderCount);
        for (var i = 0; i < SeedCatalog.OrderCount; i++)
        {
            var u = (i + f.Random.Double(0, 1)) / SeedCatalog.OrderCount * totalDayWeight;
            var found = dayCum.FindIndex(c => u < c);
            var daysAgo = found == -1 ? dayCum.Count - 1 : found;
            long ts;
            do
            {
                var hour = PickWeighted(f, hourCum);
                ts = startOfToday - (daysAgo * DayMs) + (hour * HourMs) + f.Random.Int(0, (int)(HourMs - 1000));
            }
            while (ts > nowMs);

            timestamps.Add(ts / 1000 * 1000);
        }

        timestamps.Sort();

        var itemCountCum = new List<double> { 50, 80, 95, 100 };
        var quantityCum = new List<double> { 80, 95, 100 };
        var paymentCum = new List<double> { 65, 85, 100 };
        PaymentMethod[] payments = [PaymentMethod.Card, PaymentMethod.PayPal, PaymentMethod.ApplePay];

        var orders = new List<Order>(SeedCatalog.OrderCount);
        for (var i = 0; i < timestamps.Count; i++)
        {
            var createdMs = timestamps[i];
            var (customer, address, _) = customers[PickWeighted(f, customerCum)];

            var itemCount = PickWeighted(f, itemCountCum) + 1;
            var chosen = new List<int>(itemCount);
            while (chosen.Count < itemCount)
            {
                var idx = PickWeighted(f, productCum);
                if (!chosen.Contains(idx))
                {
                    chosen.Add(idx);
                }
            }

            var items = chosen.Select(idx =>
            {
                var p = products[idx];
                return new OrderItem
                {
                    ProductId = p.Id,
                    Name = p.Name,
                    ImageUrl = p.ImageUrl,
                    Sku = p.Sku,
                    Quantity = PickWeighted(f, quantityCum) + 1,
                    UnitPrice = p.Price,
                };
            }).ToList();

            var subtotal = Money.Round2(items.Sum(it => it.Quantity * it.UnitPrice));
            var shipping = subtotal >= 100m ? 0m : 7.99m;
            var tax = Money.Round2(subtotal * 0.08m);
            var total = Money.Round2(subtotal + shipping + tax);
            var history = CreateHistory(f, createdMs, nowMs);

            orders.Add(new Order
            {
                Id = $"ord_{i + 1:D6}",
                Number = 1001 + i,
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                CustomerEmail = customer.Email,
                CustomerAvatarUrl = customer.AvatarUrl,
                Items = items,
                Subtotal = subtotal,
                Shipping = shipping,
                Tax = tax,
                Total = total,
                Status = history[^1].Status,
                PaymentMethod = payments[PickWeighted(f, paymentCum)],
                CreatedAt = FromMs(createdMs),
                ShippingAddress = new Address
                {
                    Line1 = address.Line1,
                    City = address.City,
                    Country = address.Country,
                    CountryCode = address.CountryCode,
                    PostalCode = address.PostalCode,
                },
                History = history,
            });
        }

        return orders;
    }

    /// <summary>Builds a plausible status timeline whose final status depends on the order's age.</summary>
    private static List<StatusChange> CreateHistory(Faker f, long createdMs, long nowMs)
    {
        var ageMs = nowMs - createdMs;
        var ageDays = (double)ageMs / DayMs;

        OrderStatus target;
        if (f.Random.Bool(0.04f))
        {
            target = OrderStatus.Cancelled;
        }
        else
        {
            var band = SeedCatalog.StatusByAge.FirstOrDefault(b => ageDays < b.MaxAgeDays);
            target = band is null ? OrderStatus.Delivered : PickStatus(f, band.Odds);
        }

        var steps = new List<Step> { new(OrderStatus.New, 0, "Order placed") };
        if (target == OrderStatus.Cancelled)
        {
            if (f.Random.Bool(0.4f))
            {
                steps.Add(new Step(OrderStatus.Packing, f.Random.Int(1, 8) * HourMs));
            }

            steps.Add(new Step(
                OrderStatus.Cancelled,
                f.Random.Int(1, 20) * HourMs,
                Pick(f, SeedCatalog.CancellationNotes)));
        }
        else
        {
            OrderStatus[] flow = [OrderStatus.Packing, OrderStatus.Shipped, OrderStatus.Delivered];
            long[] delays =
            [
                f.Random.Int(2, 12) * HourMs,
                f.Random.Int(12, 36) * HourMs,
                f.Random.Int(48, 120) * HourMs,
            ];
            var last = Array.IndexOf(flow, target);
            for (var k = 0; k <= last; k++)
            {
                var note = flow[k] == OrderStatus.Shipped ? $"Shipped via {Pick(f, SeedCatalog.Carriers)}" : null;
                steps.Add(new Step(flow[k], delays[k], note));
            }
        }

        // Compress the timeline if it would run past "now" (young orders).
        var totalDelay = steps.Sum(s => s.DelayMs);
        var scale = totalDelay > ageMs * 0.9 ? ageMs * 0.9 / totalDelay : 1;

        var t = createdMs;
        return steps.Select(s =>
        {
            t += (long)Math.Floor(s.DelayMs * scale);
            return new StatusChange { Status = s.Status, At = FromMs(t), Note = s.Note };
        }).ToList();
    }

    private static OrderStatus PickStatus(Faker f, IReadOnlyList<(OrderStatus Status, double Weight)> odds) =>
        odds[PickWeighted(f, Cumulate(odds.Select(o => o.Weight)))].Status;

    private static void ApplyProductSales(List<Product> products, List<Order> orders)
    {
        var byId = products.ToDictionary(p => p.Id);
        foreach (var o in orders)
        {
            if (o.Status == OrderStatus.Cancelled)
            {
                continue;
            }

            foreach (var it in o.Items)
            {
                if (byId.TryGetValue(it.ProductId, out var p))
                {
                    p.Sold += it.Quantity;
                }
            }
        }
    }

    private static List<Customer> ApplyCustomerAggregates(Faker f, List<CustomerSeed> seeds, List<Order> orders)
    {
        var byId = seeds.ToDictionary(s => s.Customer.Id, s => s.Customer);
        var firstOrder = new Dictionary<string, DateTime>();

        // Orders are chronological, so the last non-cancelled one wins lastOrderAt.
        foreach (var o in orders)
        {
            if (!byId.TryGetValue(o.CustomerId, out var c))
            {
                continue;
            }

            firstOrder.TryAdd(c.Id, o.CreatedAt);
            if (o.Status == OrderStatus.Cancelled)
            {
                continue;
            }

            c.OrdersCount += 1;
            c.LifetimeValue += o.Total;
            c.LastOrderAt = o.CreatedAt;
        }

        foreach (var c in byId.Values)
        {
            c.LifetimeValue = Money.Round2(c.LifetimeValue);

            // A customer must sign up before placing their first order.
            if (firstOrder.TryGetValue(c.Id, out var first) && c.CreatedAt > first)
            {
                c.CreatedAt = FromMs(ToMs(first) - f.Random.Long(HourMs, 30 * DayMs));
            }
        }

        return seeds.Select(s => s.Customer).ToList();
    }

    /// <summary>Rounds a price to a retail-looking value ending in .99 (or a whole amount for higher prices).</summary>
    private static decimal RetailPrice(Faker f, double raw)
    {
        // Math.Floor(x + 0.5) mirrors JS Math.round.
        var whole = (decimal)Math.Max(1, Math.Floor(raw + 0.5));
        if (whole >= 100 && f.Random.Bool(0.3f))
        {
            return whole;
        }

        return whole - 0.01m;
    }

    /// <summary>Picks an index from cumulative weights using the seeded randomizer.</summary>
    private static int PickWeighted(Faker f, List<double> cumulative)
    {
        var r = f.Random.Double(0, cumulative[^1]);
        var idx = cumulative.FindIndex(c => r < c);
        return idx == -1 ? cumulative.Count - 1 : idx;
    }

    private static List<double> Cumulate(IEnumerable<double> weights)
    {
        var sum = 0.0;
        return weights.Select(w => sum += w).ToList();
    }

    private static T Pick<T>(Faker f, IReadOnlyList<T> items) => items[f.Random.Int(0, items.Count - 1)];

    private static long ToMs(DateTime utc) => (utc - DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerMillisecond;

    private static DateTime FromMs(long ms) => DateTime.UnixEpoch.AddMilliseconds(ms);
}
