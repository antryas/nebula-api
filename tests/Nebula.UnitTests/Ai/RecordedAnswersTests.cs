using Nebula.Application.Ai;

namespace Nebula.UnitTests.Ai;

public sealed class RecordedAnswersTests
{
    [Theory]
    [InlineData("What were my top 5 products this month?", RecordedQuestion.TopProducts)]
    [InlineData("How did revenue change compared to the previous period?", RecordedQuestion.RevenueChange)]
    [InlineData("Which orders are still waiting to be shipped?", RecordedQuestion.AwaitingShipment)]
    [InlineData("Who are my most valuable customers?", RecordedQuestion.TopCustomers)]
    public void Suggested_questions_match_exactly(string question, RecordedQuestion expected) =>
        Assert.Equal(expected, RecordedAnswers.Match(question));

    [Theory]
    [InlineData("  WHAT WERE MY TOP 5 PRODUCTS THIS MONTH  ", RecordedQuestion.TopProducts)]
    [InlineData("what were my top-5 products, this month", RecordedQuestion.TopProducts)]
    [InlineData("Show me the best selling products", RecordedQuestion.TopProducts)]
    [InlineData("revenue vs last period?", RecordedQuestion.RevenueChange)]
    [InlineData("Any pending orders?", RecordedQuestion.AwaitingShipment)]
    [InlineData("What hasn't shipped yet... which orders are waiting?", RecordedQuestion.AwaitingShipment)]
    [InlineData("List my top customers", RecordedQuestion.TopCustomers)]
    [InlineData("Most valuable buyers", RecordedQuestion.TopCustomers)]
    public void Small_variations_match_by_keyword(string question, RecordedQuestion expected) =>
        Assert.Equal(expected, RecordedAnswers.Match(question));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Write me a poem about the sea")]
    [InlineData("Which products are low on stock?")]
    public void Anything_else_does_not_match(string? question) =>
        Assert.Equal(RecordedQuestion.None, RecordedAnswers.Match(question));

    [Fact]
    public void Top_products_are_listed_with_revenue_and_units()
    {
        var answer = RecordedAnswers.FormatTopProducts(
        [
            new TopProductRow("Aurora Hoodie", "Apparel", 42, 2519.58m),
            new TopProductRow("Nova Sneakers", "Footwear", 1200, 1234.5m),
        ]);

        Assert.StartsWith("Your top 2 products by revenue over the last 30 days:", answer, StringComparison.Ordinal);
        Assert.Contains("- **Aurora Hoodie** (Apparel) — $2,519.58 from 42 units", answer, StringComparison.Ordinal);
        Assert.Contains("- **Nova Sneakers** (Footwear) — $1,234.50 from 1,200 units", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Revenue_change_states_direction_and_previous_value()
    {
        var answer = RecordedAnswers.FormatRevenueChange(
            new SalesOverviewResult("30d", 12000m, 10000m, 20, 300, 280, 7.14, 40m, 2.63m));

        Assert.StartsWith(
            "Revenue over the last 30 days was **$12,000.00**, up **20%** compared with the previous 30 days ($10,000.00).",
            answer,
            StringComparison.Ordinal);
        Assert.Contains("- Orders: 300 (+7.1% vs. 280)", answer, StringComparison.Ordinal);
        Assert.Contains("- Average order value: $40.00", answer, StringComparison.Ordinal);
        Assert.Contains("- Conversion rate: 2.63%", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Revenue_decrease_says_down()
    {
        var answer = RecordedAnswers.FormatRevenueChange(new SalesOverviewResult("30d", 900m, 1000m, -10, 9, 10, -10, 100m, 1m));

        Assert.Contains("down **10%**", answer, StringComparison.Ordinal);
        Assert.Contains("(-10% vs. 10)", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Awaiting_shipment_lists_the_oldest_orders_and_the_total()
    {
        var answer = RecordedAnswers.FormatAwaitingShipment(
            new OrderListResult(17, [new OrderRow(4711, "Ada Lovelace", 99.9m, "packing", "2026-09-20")]));

        Assert.StartsWith("**17** orders are waiting to be shipped", answer, StringComparison.Ordinal);
        Assert.Contains("- **#4711** — Ada Lovelace, $99.90, packing, placed 2026-09-20", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_awaiting_shipment_is_a_short_sentence() =>
        Assert.Equal(
            "All caught up: no orders are waiting to be shipped.",
            RecordedAnswers.FormatAwaitingShipment(new OrderListResult(0, [])));

    [Fact]
    public void Top_customers_show_lifetime_value_and_order_count()
    {
        var answer = RecordedAnswers.FormatTopCustomers(
        [
            new CustomerRow("Grace Hopper", "United States", 12, 3456.78m, "2026-09-01"),
            new CustomerRow("Alan Turing", "United Kingdom", 1, 999m, null),
        ]);

        Assert.Contains("- **Grace Hopper** (United States) — $3,456.78 over 12 orders", answer, StringComparison.Ordinal);
        Assert.Contains("- **Alan Turing** (United Kingdom) — $999.00 over 1 order", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Answers_use_no_tables_or_html()
    {
        var answers = new[]
        {
            RecordedAnswers.FormatTopProducts([new TopProductRow("A", "Home", 1, 1m)]),
            RecordedAnswers.FormatRevenueChange(new SalesOverviewResult("30d", 1m, 1m, 0, 1, 1, 0, 1m, 1m)),
            RecordedAnswers.FormatAwaitingShipment(new OrderListResult(1, [new OrderRow(1, "B", 1m, "new", "2026-09-24")])),
            RecordedAnswers.FormatTopCustomers([new CustomerRow("C", "D", 1, 1m, null)]),
        };

        Assert.All(answers, a =>
        {
            Assert.DoesNotContain('|', a);
            Assert.DoesNotContain('<', a);
        });
    }
}
