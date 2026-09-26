using Microsoft.Extensions.Options;
using Nebula.Application.Common;

namespace Nebula.Application.Ai;

/// <summary>
/// In-memory daily allowance for live provider calls: per client IP and for all clients together, reset at
/// midnight UTC. One live request (including its tool round trips) costs one unit; status checks and recorded
/// answers are free. Registered as a singleton; counters live only as long as the process, which is fine for a
/// single-instance demo whose real spending cap is the prepaid provider balance.
/// </summary>
public sealed class AiQuotaTracker(IClock clock, IOptions<AiOptions> options)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _perClient = new(StringComparer.Ordinal);
    private DateOnly _day;
    private int _total;

    /// <summary>What <paramref name="client"/> has left today, without consuming anything.</summary>
    public AiQuota Peek(string client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            RollOver();
            return Snapshot(client);
        }
    }

    /// <summary>
    /// Atomically takes one unit from both allowances. Returns false (and takes nothing) when either is used up.
    /// </summary>
    public bool TryConsume(string client, out AiQuota quota)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            RollOver();
            var (perClientLimit, totalLimit) = Limits();
            var used = _perClient.GetValueOrDefault(client);
            if (used >= perClientLimit || _total >= totalLimit)
            {
                quota = Snapshot(client);
                return false;
            }

            _perClient[client] = used + 1;
            _total++;
            quota = Snapshot(client);
            return true;
        }
    }

    private AiQuota Snapshot(string client)
    {
        var (perClientLimit, totalLimit) = Limits();
        var clientLeft = perClientLimit - _perClient.GetValueOrDefault(client);
        var totalLeft = totalLimit - _total;
        return new AiQuota(Math.Max(0, Math.Min(clientLeft, totalLeft)), perClientLimit);
    }

    private (int PerClient, int Total) Limits()
    {
        var value = options.Value;
        return (Math.Max(0, value.DailyRequestsPerClient), Math.Max(0, value.DailyRequestsTotal));
    }

    /// <summary>Forgets yesterday's counters (and clients) on the first call of a new UTC day.</summary>
    private void RollOver()
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (today != _day)
        {
            _day = today;
            _perClient.Clear();
            _total = 0;
        }
    }
}
