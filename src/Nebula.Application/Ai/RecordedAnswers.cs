using System.Globalization;
using System.Text;

namespace Nebula.Application.Ai;

/// <summary>The suggested questions that recorded mode can answer from store data.</summary>
public enum RecordedQuestion
{
    None,
    TopProducts,
    RevenueChange,
    AwaitingShipment,
    TopCustomers,
}

/// <summary>
/// Recorded mode: no model, just templates filled with real numbers from <see cref="StoreDataTools"/>. Used when no
/// API key is configured, the daily quota is used up, or the provider fails or times out.
/// </summary>
public static class RecordedAnswers
{
    public const string Fallback =
        "Live AI is not available right now. Try one of the suggested questions — they are answered from live store data.";

    /// <summary>The questions the Angular app offers as suggestions, in display order.</summary>
    public static readonly IReadOnlyList<(string Text, RecordedQuestion Question)> Suggestions =
    [
        ("What were my top 5 products this month?", RecordedQuestion.TopProducts),
        ("How did revenue change compared to the previous period?", RecordedQuestion.RevenueChange),
        ("Which orders are still waiting to be shipped?", RecordedQuestion.AwaitingShipment),
        ("Who are my most valuable customers?", RecordedQuestion.TopCustomers),
    ];

    private const int RecordedRows = 5;

    /// <summary>
    /// Exact suggestion first (case-, punctuation- and whitespace-insensitive), then keywords so small rewordings
    /// still match: top/best + product, revenue, waiting/shipped/pending, customers/valuable.
    /// </summary>
    public static RecordedQuestion Match(string? question)
    {
        var text = Normalize(question);
        if (text.Length == 0)
        {
            return RecordedQuestion.None;
        }

        foreach (var (suggestion, kind) in Suggestions)
        {
            if (Normalize(suggestion) == text)
            {
                return kind;
            }
        }

        var words = text.Split(' ');
        bool Has(string stem) => words.Any(w => w.StartsWith(stem, StringComparison.Ordinal));

        if ((Has("top") || Has("best")) && Has("product"))
        {
            return RecordedQuestion.TopProducts;
        }

        if (Has("revenue"))
        {
            return RecordedQuestion.RevenueChange;
        }

        if (Has("waiting") || Has("shipped") || Has("pending"))
        {
            return RecordedQuestion.AwaitingShipment;
        }

        return Has("customer") || Has("valuable") ? RecordedQuestion.TopCustomers : RecordedQuestion.None;
    }

    /// <summary>Answers <paramref name="question"/> from store data; unknown questions get <see cref="Fallback"/>.</summary>
    public static async Task<(string Answer, IReadOnlyList<string> ToolsUsed)> AnswerAsync(
        string? question, StoreDataTools tools, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tools);

        switch (Match(question))
        {
            case RecordedQuestion.TopProducts:
                return (FormatTopProducts(await tools.GetTopProductsAsync("30d", RecordedRows, ct)), [StoreDataTools.TopProducts]);
            case RecordedQuestion.RevenueChange:
                return (FormatRevenueChange(await tools.GetSalesOverviewAsync("30d", ct)), [StoreDataTools.SalesOverview]);
            case RecordedQuestion.AwaitingShipment:
                return (
                    FormatAwaitingShipment(await tools.ListOrdersAsync("new,packing", RecordedRows, oldestFirst: true, ct)),
                    [StoreDataTools.ListOrders]);
            case RecordedQuestion.TopCustomers:
                return (FormatTopCustomers(await tools.GetTopCustomersAsync(RecordedRows, ct)), [StoreDataTools.TopCustomers]);
            default:
                return (Fallback, []);
        }
    }

    public static string FormatTopProducts(IReadOnlyList<TopProductRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return "There were no paid orders in the last 30 days, so there are no top products yet.";
        }

        var text = new StringBuilder(Invariant($"Your top {rows.Count} products by revenue over the last 30 days:\n\n"));
        foreach (var row in rows)
        {
            text.Append(Invariant($"- **{row.Name}** ({row.Category}) — {Usd(row.Revenue)} from {row.UnitsSold:N0} units\n"));
        }

        return text.ToString().TrimEnd();
    }

    public static string FormatRevenueChange(SalesOverviewResult overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        var direction = overview.RevenueChangePct switch
        {
            > 0 => Invariant($"up **{overview.RevenueChangePct:0.#}%**"),
            < 0 => Invariant($"down **{-overview.RevenueChangePct:0.#}%**"),
            _ => "flat",
        };
        return Invariant(
            $"Revenue over the last 30 days was **{Usd(overview.Revenue)}**, {direction} compared with the previous 30 days ({Usd(overview.PreviousRevenue)}).\n\n")
            + Invariant($"- Orders: {overview.Orders:N0} ({Signed(overview.OrdersChangePct)} vs. {overview.PreviousOrders:N0})\n")
            + Invariant($"- Average order value: {Usd(overview.AverageOrderValue)}\n")
            + Invariant($"- Conversion rate: {overview.ConversionPct:0.##}%");
    }

    public static string FormatAwaitingShipment(OrderListResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Total == 0)
        {
            return "All caught up: no orders are waiting to be shipped.";
        }

        var noun = result.Total == 1 ? "order is" : "orders are";
        var text = new StringBuilder(Invariant($"**{result.Total:N0}** {noun} waiting to be shipped (status new or packing). "));
        text.Append(result.Orders.Count == 1 ? "The oldest one:\n\n" : "The oldest ones:\n\n");
        foreach (var order in result.Orders)
        {
            text.Append(Invariant($"- **#{order.Number}** — {order.Customer}, {Usd(order.Total)}, {order.Status}, placed {order.CreatedAt}\n"));
        }

        return text.ToString().TrimEnd();
    }

    public static string FormatTopCustomers(IReadOnlyList<CustomerRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return "There are no customers yet.";
        }

        var text = new StringBuilder("Your most valuable customers by lifetime value:\n\n");
        foreach (var row in rows)
        {
            var orders = row.Orders == 1 ? "1 order" : Invariant($"{row.Orders:N0} orders");
            text.Append(Invariant($"- **{row.Name}** ({row.Country}) — {Usd(row.LifetimeValue)} over {orders}\n"));
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Lower case, letters and digits only, single spaces.</summary>
    internal static string Normalize(string? text)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Usd(decimal amount) => Invariant($"${amount:N2}");

    private static string Signed(double pct) => pct > 0 ? Invariant($"+{pct:0.#}%") : Invariant($"{pct:0.#}%");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
