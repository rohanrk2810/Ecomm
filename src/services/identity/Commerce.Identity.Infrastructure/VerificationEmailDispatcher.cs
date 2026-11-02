using System.Security.Cryptography;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
namespace Commerce.Identity.Infrastructure;

public sealed class VerificationDeliverySettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool StartTls { get; set; } = true;
    public string FromAddress { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int MaxAttempts { get; set; } = 5;
}
public sealed class VerificationEmailDispatcher(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    IOptions<VerificationDeliverySettings> settings, TimeProvider clock,
    ILogger<VerificationEmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
                await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                var now = clock.GetUtcNow();
                var messages = await db.VerificationEmails.FromSqlInterpolated($"SELECT * FROM identity.\"VerificationEmails\" WHERE \"DeliveredAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL AND \"AbandonedAtUtc\" IS NULL AND \"NextAttemptAtUtc\" <= {now} ORDER BY \"NextAttemptAtUtc\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(stoppingToken);
                if (messages.Count > 0)
                {
                    var row = messages[0];
                    if (row.ExpiresAtUtc <= now) row.CancelledAtUtc = now;
                    else
                    {
                        row.Attempts++;
                        try
                        {
                            var payload = JsonSerializer.Deserialize<VerificationEmailContent>(protection.CreateProtector(VerificationIssuer.MailPurpose).Unprotect(row.ProtectedPayload))
                                ?? throw new InvalidOperationException("Invalid verification delivery payload.");
                            var from = MailboxAddress.Parse(options.FromAddress);
                            var message = new MimeMessage { Subject = "Verify your email address", MessageId = row.Id.ToString("N") + "@" + from.Address.Split('@')[1] };
                            message.From.Add(from); message.To.Add(MailboxAddress.Parse(payload.Recipient));
                            message.Body = new TextPart("plain") { Text = "Verify your email address by opening this link and selecting Verify email:\n\n" + payload.Link + "\n\nThis link expires. If you did not request this, ignore this email." };
                            using var smtp = new SmtpClient { Timeout = 10000 };
                            await smtp.ConnectAsync(options.Host, options.Port, options.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, stoppingToken);
                            if (!string.IsNullOrEmpty(options.Username)) await smtp.AuthenticateAsync(options.Username,
                                options.Password ?? throw new InvalidOperationException("SMTP credentials are incomplete."), stoppingToken);
                            await smtp.SendAsync(message, stoppingToken);
                            await smtp.DisconnectAsync(true, stoppingToken);
                            row.DeliveredAtUtc = clock.GetUtcNow();
                        }
                        catch (Exception error) when (error is IOException or MailKit.CommandException or MailKit.ProtocolException or CryptographicException or JsonException or System.Net.Sockets.SocketException)
                        {
                            row.NextAttemptAtUtc = now.AddSeconds(Math.Min(300, 10 * Math.Pow(2, row.Attempts - 1)));
                            if (row.Attempts >= options.MaxAttempts) row.AbandonedAtUtc = now;
                            logger.LogError("Verification email {MessageId} delivery deferred; attempt {Attempt}, abandoned {Abandoned}, category {Category}", row.Id, row.Attempts, row.AbandonedAtUtc is not null, error.GetType().Name);
                        }
                    }
                    await db.SaveChangesAsync(stoppingToken);
                }
                await transaction.CommitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is Npgsql.NpgsqlException or DbUpdateException)
            { logger.LogError("Verification delivery database unavailable; category {Category}", error.GetType().Name); }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
