namespace IntegrationDaemon.Services;

public interface IStatusPublisher
{
    Task PublishSmappeeAsync(string payload, CancellationToken cancellationToken = default);
    Task PublishSigenergyAsync(string payload, CancellationToken cancellationToken = default);
}

public class DbOnlyStatusPublisher : IStatusPublisher
{
    public Task PublishSmappeeAsync(string payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PublishSigenergyAsync(string payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class NoopMqttStatusPublisher : IStatusPublisher
{
    private readonly ILogger<NoopMqttStatusPublisher> _logger;

    public NoopMqttStatusPublisher(ILogger<NoopMqttStatusPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishSmappeeAsync(string payload, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("MQTT publisher disabled; skipped Smappee publish");
        return Task.CompletedTask;
    }

    public Task PublishSigenergyAsync(string payload, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("MQTT publisher disabled; skipped Sigenergy publish");
        return Task.CompletedTask;
    }
}
