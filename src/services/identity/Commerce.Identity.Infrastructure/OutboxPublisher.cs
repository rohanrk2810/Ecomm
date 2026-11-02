using System.Text.Json;
using Commerce.Identity.Contracts;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Commerce.Identity.Infrastructure;

public sealed class MessagingSettings
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string RegistrationTopic { get; set; } = string.Empty;
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public bool PublisherEnabled { get; set; } = true;
}
public sealed class OutboxPublisher(IServiceScopeFactory scopes, IOptions<MessagingSettings> settings,
    ILogger<OutboxPublisher> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        if (!options.PublisherEnabled) return;
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageTimeoutMs = 5000,
            AllowAutoCreateTopics = false
        }).SetErrorHandler((_, error) => logger.LogWarning("Kafka producer error {Code}", error.Code)).Build();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
                await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                var rows = await db.Outbox.FromSqlRaw(
                    "SELECT * FROM identity.\"Outbox\" WHERE \"PublishedAtUtc\" IS NULL ORDER BY \"CreatedAtUtc\" LIMIT 1 FOR UPDATE SKIP LOCKED")
                    .ToListAsync(stoppingToken);
                if (rows.Count > 0)
                {
                    var message = rows[0];
                    var envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(message.Payload,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))
                        ?? throw new InvalidOperationException("Invalid outbox envelope.");
                    message.Attempts++;
                    try
                    {
                        await producer.ProduceAsync(options.RegistrationTopic, new Message<string, string>
                        {
                            Key = envelope.AggregateId,
                            Value = message.Payload,
                            Headers = new Headers { { "event-id", System.Text.Encoding.UTF8.GetBytes(message.Id.ToString()) } }
                        }, stoppingToken);
                        message.PublishedAtUtc = clock.GetUtcNow();
                    }
                    catch (ProduceException<string, string> error)
                    {
                        logger.LogWarning("Outbox event {EventId} pending after Kafka error {Code}", message.Id, error.Error.Code);
                    }
                    await db.SaveChangesAsync(stoppingToken);
                }
                await transaction.CommitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is Npgsql.NpgsqlException or DbUpdateException)
            {
                logger.LogError("Identity outbox database unavailable; error category {Category}", error.GetType().Name);
            }
            await Task.Delay(options.PollIntervalMilliseconds, stoppingToken);
        }
    }
}
