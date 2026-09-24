using System.Net;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

/// <summary>Changes data: <see cref="SeedRestorer"/> restores the seed when the class is done, so the shared database stays pristine.</summary>
[Collection(ApiCollection.Name)]
public sealed class LiveEndpointsTests(NebulaApiFactory factory) : IClassFixture<SeedRestorer>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync() => _client = await factory.CreateAuthenticatedClientAsync();

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Tick_creates_a_new_order_that_leads_the_default_list()
    {
        var ct = TestContext.Current.CancellationToken;

        using var response = await _client.PostAsync("/api/live/tick", null, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.ReadJsonAsync();
        var id = order.GetProperty("id").GetString();
        Assert.Equal($"/api/orders/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("new", order.GetProperty("status").GetString());
        Assert.Equal("2026-09-24T12:00:00Z", order.GetProperty("createdAt").GetString());
        Assert.Equal("Order placed", order.GetProperty("history")[0].GetProperty("note").GetString());
        Assert.InRange(order.GetProperty("items").GetArrayLength(), 1, 3);

        using var list = await _client.GetAsync("/api/orders", ct);
        var page = await list.ReadJsonAsync();
        Assert.Equal(4801, page.GetProperty("total").GetInt32());
        Assert.Equal(id, page.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(order.GetProperty("number").GetInt32(), page.GetProperty("items")[0].GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Ticks_racing_a_demo_reset_all_succeed()
    {
        var ct = TestContext.Current.CancellationToken;

        var ticks = Enumerable.Range(0, 12).Select(_ => _client.PostAsync("/api/live/tick", null, ct)).ToList();
        var reset = _client.PostAsync("/api/demo/reset", null, ct);
        var responses = await Task.WhenAll(ticks.Append(reset));

        try
        {
            Assert.All(responses.Take(ticks.Count), r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            Assert.Equal(HttpStatusCode.NoContent, responses[^1].StatusCode);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            // Leave the seed intact for the other test in this class.
            using var cleanup = await _client.PostAsync("/api/demo/reset", null, ct);
        }
    }
}
