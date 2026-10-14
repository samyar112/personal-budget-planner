using System.Threading.Channels;

namespace backend.Services.Email;

/// <summary>
/// Hands account emails to a background worker. Endpoints that must answer identically for
/// known and unknown addresses enqueue here instead of doing the work inline, so response
/// time cannot reveal whether an account exists.
/// </summary>
public interface IAccountEmailQueue
{
    ValueTask EnqueueAsync(AccountEmailJob job, CancellationToken cancellationToken);
}

/// <summary>
/// In-memory bounded queue. Jobs still queued at shutdown are lost; the user can ask again.
/// </summary>
public sealed class ChannelAccountEmailQueue(ILogger<ChannelAccountEmailQueue> logger) : IAccountEmailQueue
{
    private const int Capacity = 100;

    private readonly Channel<AccountEmailJob> _channel = Channel.CreateBounded<AccountEmailJob>(
        new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public ChannelReader<AccountEmailJob> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(AccountEmailJob job, CancellationToken cancellationToken)
    {
        // Never block or fail the request: a full queue drops the job, like a lost email.
        if (!_channel.Writer.TryWrite(job))
        {
            logger.LogWarning("Account email queue is full; dropped a {Kind} job.", job.Kind);
        }

        return ValueTask.CompletedTask;
    }
}
