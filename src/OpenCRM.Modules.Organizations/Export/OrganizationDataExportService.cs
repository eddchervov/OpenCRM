using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Organizations.Export;

/// <summary> Управляет сохранённой очередью и повторным скачиванием готовых архивов организации. </summary>
internal sealed partial class OrganizationDataExportService(
    OpenCRMContext context,
    IFileStorageService storage,
    OrganizationDataExportOptions options,
    ILogger<OrganizationDataExportService> logger) : IOrganizationDataExportService
{
    private const long BuildLockKey = 0x4D4F544558500001;
    private const long AdmissionLockKey = 0x4D4F544558500002;
    private const long DownloadLockKey = 0x4D4F544558500010;

    public async Task<OrganizationDataExportStatusDto> RequestAsync(
        string userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        this.ValidateJobOptions();
        await using var db = this.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Общая короткая блокировка ограничивает очередь и исключает гонку между организациями.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdmissionLockKey})", cancellationToken);
        await OrganizationDataExportService.EnsureOwnerAsync(db, userId, organizationId, cancellationToken);
        var now = DateTime.UtcNow;
        var active = await db.Set<OrganizationDataExportJob>().AsNoTracking()
            .Where(job => job.OrganizationId == organizationId && (job.State == "queued" || job.State == "running"))
            .OrderByDescending(job => job.RequestedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (active != null)
        {
            return this.MapStatus(active, now);
        }

        var latest = await db.Set<OrganizationDataExportJob>().AsNoTracking()
            .Where(job => job.OrganizationId == organizationId)
            .OrderByDescending(job => job.RequestedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var successful = await db.Set<OrganizationDataExportJob>().AsNoTracking()
            .Where(job => job.OrganizationId == organizationId && job.CompletedAtUtc > now.AddDays(-1))
            .OrderByDescending(job => job.CompletedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (successful != null)
        {
            return this.MapStatus(successful, now);
        }
        if (latest?.State == "failed" && latest.FinishedAtUtc?.AddSeconds(options.RetryDelaySeconds) > now)
        {
            return this.MapStatus(latest, now);
        }
        if (await db.Set<OrganizationDataExportJob>().CountAsync(
            job => job.State == "queued" || job.State == "running", cancellationToken) >= options.QueueLimit)
        {
            throw new OrganizationDataExportResourceLimitException("Очередь выгрузок занята. Повторите запрос позже.");
        }

        storage.ValidatePrivateStorage();
        var request = new OrganizationDataExportJob
        {
            OrganizationId = organizationId,
            RequestedByUserId = userId,
            RequestedAtUtc = now
        };
        db.Add(request);
        db.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            OrganizationId = organizationId, ActorUserId = userId,
            EventType = "organization.data-export.queued", Title = "Выгрузка данных поставлена в очередь", CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return this.MapStatus(request, now);
    }

    public async Task<OrganizationDataExportStatusDto> GetStatusAsync(
        string userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        await using var db = this.CreateContext();
        await OrganizationDataExportService.EnsureOwnerAsync(db, userId, organizationId, cancellationToken);
        var latest = await db.Set<OrganizationDataExportJob>().AsNoTracking()
            .Where(job => job.OrganizationId == organizationId)
            .OrderByDescending(job => job.RequestedAtUtc).FirstOrDefaultAsync(cancellationToken);

        return latest == null ? new OrganizationDataExportStatusDto() : this.MapStatus(latest, DateTime.UtcNow);
    }

    public async Task<Stream> OpenAsync(
        string userId, Guid organizationId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var db = this.CreateContext();
        await OrganizationDataExportService.EnsureOwnerAsync(db, userId, organizationId, cancellationToken);
        var job = await db.Set<OrganizationDataExportJob>().AsNoTracking()
            .SingleOrDefaultAsync(job => job.Id == jobId && job.OrganizationId == organizationId, cancellationToken);
        if (job?.State != "ready" || job.ExpiresAtUtc == null || job.ExpiresAtUtc <= DateTime.UtcNow
            || job.StoragePath != $"organization-exports/{organizationId:N}/{jobId:N}.zip")
        {
            throw new InvalidOperationException("Архив недоступен. Обновите состояние выгрузки в настройках организации.");
        }

        OrganizationDataExportLock? lease = null;
        for (var slot = 0; slot < 2 && lease == null; slot++)
        {
            lease = await OrganizationDataExportLock.TryAcquireAsync(this.ConnectionString, DownloadLockKey + slot, cancellationToken);
        }
        if (lease == null)
        {
            throw new OrganizationDataExportResourceLimitException("Сейчас скачиваются другие архивы. Повторите скачивание позже.");
        }

        try
        {
            var stream = await storage.GetPrivateFileAsync(job.StoragePath, cancellationToken);

            return new OrganizationDataExportReadStream(stream, lease);
        }
        catch (FileNotFoundException)
        {
            await lease.DisposeAsync();
            // Потеря готового объекта не должна закрывать право повторной подготовки на сутки.
            await db.Set<OrganizationDataExportJob>().Where(value => value.Id == job.Id && value.State == "ready")
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.State, "failed")
                    .SetProperty(value => value.CompletedAtUtc, (DateTime?)null)
                    .SetProperty(value => value.FinishedAtUtc, DateTime.UtcNow)
                    .SetProperty(value => value.ErrorMessage, "Готовый архив отсутствует. Запросите подготовку ещё раз."), CancellationToken.None);
            throw;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    private string ConnectionString => context.Database.GetConnectionString()
        ?? throw new InvalidOperationException("Не настроено подключение для выгрузки данных.");

    private OpenCRMContext CreateContext() => new(new DbContextOptionsBuilder<OpenCRMContext>()
        .UseNpgsql(context.Database.GetDbConnection()).Options);

    private static async Task EnsureOwnerAsync(
        OpenCRMContext db, string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        if (!await db.Organizations.AsNoTracking().AnyAsync(organization => organization.Id == organizationId
            && !organization.IsDeleted && organization.OwnerUserId == userId
            && organization.Members.Any(member => member.UserId == userId), cancellationToken))
        {
            throw new UnauthorizedAccessException("Выгрузка доступна только владельцу организации.");
        }
    }

    private OrganizationDataExportStatusDto MapStatus(OrganizationDataExportJob job, DateTime now) => new()
    {
        Id = job.Id,
        State = job.State == "ready" && job.ExpiresAtUtc <= now ? "expired" : job.State,
        RequestedAtUtc = job.RequestedAtUtc,
        CompletedAtUtc = job.CompletedAtUtc,
        ExpiresAtUtc = job.ExpiresAtUtc,
        RetryAtUtc = job.CompletedAtUtc?.AddDays(1)
            ?? (job.State == "failed" ? job.FinishedAtUtc?.AddSeconds(options.RetryDelaySeconds) : null),
        SizeBytes = job.ArchiveSizeBytes,
        ErrorMessage = job.ErrorMessage
    };

    private void ValidateJobOptions()
    {
        if (options.QueueLimit is < 1 or > 1000 || options.RetryDelaySeconds is < 1 or > 3600
            || options.RetentionHours is < 24 or > 168 || options.TimeoutSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException("Некорректные ограничения фоновой выгрузки.");
        }
    }
}
