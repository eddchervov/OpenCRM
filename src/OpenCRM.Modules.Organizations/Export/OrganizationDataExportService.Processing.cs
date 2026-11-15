using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Organizations.Export;

internal sealed partial class OrganizationDataExportService
{
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        this.ValidateJobOptions();
        await using var lease = await OrganizationDataExportLock.TryAcquireAsync(this.ConnectionString, BuildLockKey, cancellationToken);
        if (lease == null)
        {
            return;
        }

        await using var db = this.CreateContext();
        var now = DateTime.UtcNow;
        // После получения общего слота прежний running уже не может принадлежать здоровому исполнителю.
        await db.Set<OrganizationDataExportJob>().Where(job => job.State == "running")
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.State, "failed")
                .SetProperty(job => job.FinishedAtUtc, now)
                .SetProperty(job => job.ErrorMessage, "Подготовка архива была прервана. Повторите запрос.")
                .SetProperty(job => job.CleanupAfterUtc, job => job.StoragePath == null
                    ? now.AddDays(30) : now.AddSeconds(options.TimeoutSeconds * 2)), cancellationToken);
        this.CleanupSpool();
        await this.CleanupArtifactsAsync(db, cancellationToken);
        var obsoleteIds = await db.Set<OrganizationDataExportJob>().Where(job => job.StoragePath == null
            && job.CleanupAfterUtc <= now && job.FinishedAtUtc < now.AddDays(-30)
            && job.State != "queued" && job.State != "running")
            .OrderBy(job => job.CleanupAfterUtc).Select(job => job.Id).Take(100).ToArrayAsync(cancellationToken);
        if (obsoleteIds.Length > 0)
        {
            await db.Set<OrganizationDataExportJob>().Where(job => obsoleteIds.Contains(job.Id)
                && job.StoragePath == null && job.State != "queued" && job.State != "running")
                .ExecuteDeleteAsync(cancellationToken);
        }
        var request = await db.Set<OrganizationDataExportJob>().AsNoTracking()
            .Where(job => job.State == "queued").OrderBy(job => job.RequestedAtUtc).ThenBy(job => job.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (request == null)
        {
            return;
        }

        var path = $"organization-exports/{request.OrganizationId:N}/{request.Id:N}.zip";
        var claimed = await db.Set<OrganizationDataExportJob>().Where(job => job.Id == request.Id && job.State == "queued")
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.State, "running")
                .SetProperty(job => job.StartedAtUtc, now)
                .SetProperty(job => job.StoragePath, path)
                .SetProperty(job => job.CleanupAfterUtc, now.AddSeconds(options.TimeoutSeconds * 2)), cancellationToken);
        if (claimed != 1)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            await OrganizationDataExportService.EnsureOwnerAsync(db, request.RequestedByUserId, request.OrganizationId, timeout.Token);
            storage.ValidatePrivateStorage();
            var builder = new OrganizationDataArchiveBuilder(db, storage, options);
            await using var archive = await builder.CreateAsync(request.OrganizationId, timeout.Token);
            var length = archive.Length;
            var storedPath = await storage.SavePrivateFileAsync(path, archive, timeout.Token);
            if (storedPath != path)
            {
                throw new InvalidOperationException("Хранилище изменило ключ готового архива.");
            }

            await OrganizationDataExportService.EnsureOwnerAsync(db, request.RequestedByUserId, request.OrganizationId, timeout.Token);
            var completedAt = DateTime.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
            var published = await db.Set<OrganizationDataExportJob>().Where(job => job.Id == request.Id && job.State == "running")
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.State, "ready")
                    .SetProperty(job => job.CompletedAtUtc, completedAt)
                    .SetProperty(job => job.FinishedAtUtc, completedAt)
                    .SetProperty(job => job.ExpiresAtUtc, completedAt.AddHours(options.RetentionHours))
                    .SetProperty(job => job.CleanupAfterUtc, completedAt.AddHours(options.RetentionHours))
                    .SetProperty(job => job.ArchiveSizeBytes, length), timeout.Token);
            if (published != 1)
            {
                throw new InvalidOperationException("Задание подготовки архива уже завершено другим исполнителем.");
            }

            db.OrganizationAuditEvents.Add(new OrganizationAuditEvent
            {
                OrganizationId = request.OrganizationId, ActorUserId = request.RequestedByUserId,
                EventType = "organization.data-export.completed", Title = "Подготовлена выгрузка данных организации",
                CreatedAt = completedAt
            });
            await db.SaveChangesAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
        catch (Exception exception)
        {
            var message = exception switch
            {
                OrganizationDataExportResourceLimitException => exception.Message,
                OperationCanceledException when cancellationToken.IsCancellationRequested =>
                    "Подготовка архива была прервана. Повторите запрос.",
                OperationCanceledException => "Подготовка архива превысила допустимое время. Повторите запрос позже.",
                UnauthorizedAccessException => "Доступ владельца изменился. Запросите выгрузку из актуального аккаунта.",
                _ => "Не удалось подготовить архив. Повторите запрос позже."
            };
            logger.LogWarning(exception, "Не завершена выгрузка {ExportJobId} организации {OrganizationId}.", request.Id, request.OrganizationId);
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var failedAt = DateTime.UtcNow;
            // CAS не отзывает готовый архив при неизвестном результате фиксации транзакции.
            var failed = await db.Set<OrganizationDataExportJob>().Where(job => job.Id == request.Id && job.State == "running")
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.State, "failed")
                    .SetProperty(job => job.FinishedAtUtc, failedAt)
                    .SetProperty(job => job.ErrorMessage, message)
                    .SetProperty(job => job.CleanupAfterUtc, failedAt.AddSeconds(options.TimeoutSeconds * 2)), cleanupTimeout.Token);
            if (failed == 1)
            {
                await this.DeleteArtifactAsync(db, request.Id, path, cleanupTimeout.Token);
            }
        }
    }

    private async Task CleanupArtifactsAsync(OpenCRMContext db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var retired = await db.Set<OrganizationDataExportJob>().AsNoTracking()
            .Where(job => job.StoragePath != null && job.CleanupAfterUtc <= now
                && (job.State == "ready" || job.State == "failed" || job.State == "expired"))
            .OrderBy(job => job.CleanupAfterUtc).Take(5).ToArrayAsync(cancellationToken);
        foreach (var job in retired)
        {
            await this.DeleteArtifactAsync(db, job.Id, job.StoragePath!, cancellationToken);
        }
    }

    private async Task DeleteArtifactAsync(OpenCRMContext db, Guid jobId, string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!await storage.DeletePrivateFileAsync(path, cancellationToken))
            {
                throw new IOException("Хранилище не подтвердило удаление архива.");
            }

            await db.Set<OrganizationDataExportJob>().Where(job => job.Id == jobId && job.StoragePath == path)
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.StoragePath, (string?)null)
                    .SetProperty(job => job.CleanupAfterUtc, DateTime.UtcNow.AddDays(30))
                    .SetProperty(job => job.State, job => job.State == "ready" ? "expired" : job.State), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Не удалось удалить временный архив задания {ExportJobId}.", jobId);
            await db.Set<OrganizationDataExportJob>().Where(job => job.Id == jobId)
                .ExecuteUpdateAsync(update => update.SetProperty(job => job.CleanupAfterUtc, DateTime.UtcNow.AddHours(1)), cancellationToken);
        }
    }

    private void CleanupSpool()
    {
        var directory = options.GetSpoolDirectory();
        if (!Directory.Exists(directory))
        {
            return;
        }

        var olderThan = DateTime.UtcNow.AddSeconds(-2 * options.TimeoutSeconds);
        foreach (var path in Directory.EnumerateFiles(directory, "opencrm-export-*", SearchOption.TopDirectoryOnly).Take(100))
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && File.GetLastWriteTimeUtc(path) < olderThan)
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Не удалось удалить прежний временный файл выгрузки {Path}.", path);
            }
        }
    }
}
