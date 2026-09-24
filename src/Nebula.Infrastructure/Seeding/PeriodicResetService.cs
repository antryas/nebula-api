using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nebula.Application.Common;

namespace Nebula.Infrastructure.Seeding;

/// <summary>
/// Re-seeds the public demo every <c>Demo:ResetInterval</c> (default 6 h); <c>00:00:00</c> disables it.
/// </summary>
public sealed partial class PeriodicResetService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PeriodicResetService> logger) : BackgroundService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue("Demo:ResetInterval", DefaultInterval);
        if (interval <= TimeSpan.Zero)
        {
            LogDisabled();
            return;
        }

        LogScheduled(interval);
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IDemoResetter>().ResetAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogFailed(ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Periodic demo reset is disabled")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Periodic demo reset every {Interval}")]
    private partial void LogScheduled(TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Periodic demo reset failed")]
    private partial void LogFailed(Exception exception);
}
