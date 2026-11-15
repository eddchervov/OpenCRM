using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Communications.Files;

/// <summary> Удаляет временные файлы, которые не были отправлены в течение суток. </summary>
internal sealed class ConversationAttachmentCleanupWorker(
    IServiceScopeFactory scopes,
    ILogger<ConversationAttachmentCleanupWorker> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<ConversationAttachmentCleanupWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = this._scopes.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
                var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
                var expiry = DateTime.UtcNow.AddDays(-1);
                var candidates = await context.OrganizationConversationAttachments.AsNoTracking()
                    .Where(item => item.MessageId == null && item.CreatedAt < expiry)
                    .OrderBy(item => item.CreatedAt)
                    .Take(50)
                    .Select(item => new { item.Id, item.ConversationId })
                    .ToListAsync(stoppingToken);

                foreach (var candidate in candidates)
                {
                    await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                    {
                        context.ChangeTracker.Clear();
                        await using var transaction = await context.Database.BeginTransactionAsync(stoppingToken);
                        await context.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT 1 FROM \"OrganizationConversations\" WHERE \"Id\" = {candidate.ConversationId} FOR UPDATE", stoppingToken);
                        var attachment = await context.OrganizationConversationAttachments.SingleOrDefaultAsync(item =>
                            item.Id == candidate.Id && item.MessageId == null && item.CreatedAt < expiry, stoppingToken);
                        if (attachment is null)
                        {
                            return;
                        }

                        if (attachment.StorageReference is not null)
                        {
                            if (!await storage.DeleteFileAsync(attachment.StorageReference))
                            {
                                this._logger.LogWarning("Временное вложение {AttachmentId} осталось в хранилище; очистка будет повторена.", attachment.Id);
                                return;
                            }
                        }

                        context.OrganizationConversationAttachments.Remove(attachment);
                        await context.SaveChangesAsync(stoppingToken);
                        await transaction.CommitAsync(stoppingToken);
                    });
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, "Не удалось удалить временные вложения переписки.");
            }
        }
    }
}
