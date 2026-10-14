namespace backend.Services.Email;

/// <summary>
/// Drains <see cref="ChannelAccountEmailQueue"/> one job at a time, each in its own DI scope.
/// A failing job is logged and skipped so it can never stop the worker.
/// </summary>
public sealed class AccountEmailWorker(
    ChannelAccountEmailQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AccountEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<AccountEmailProcessor>();
                await processor.ProcessAsync(job, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Account email worker failed on a {Kind} job.", job.Kind);
            }
        }
    }
}
