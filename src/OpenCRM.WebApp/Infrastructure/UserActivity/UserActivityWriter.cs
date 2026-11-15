using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.WebApp.Infrastructure.UserActivity;

/// <summary> Сохраняет события пачками и постепенно удаляет устаревшие записи. </summary>
public sealed class UserActivityWriter(
    UserActivityBuffer buffer,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<UserActivityOptions> options,
    ILogger<UserActivityWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = DateTime.UtcNow.AddMinutes(5);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await this.FlushAsync(stoppingToken);
                if (DateTime.UtcNow < nextCleanup)
                {
                    continue;
                }

                var deleted = await this.CleanupAsync(stoppingToken);
                nextCleanup = DateTime.UtcNow.Add(deleted == 2000 ? TimeSpan.FromSeconds(10) : TimeSpan.FromHours(6));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // При остановке остаток диагностических событий может быть потерян.
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Enabled)
        {
            while (buffer.TryRead(out _))
            {
            }

            return;
        }

        var activities = new List<UserActivityEvent>(200);
        while (activities.Count < 200 && buffer.TryRead(out var activity))
        {
            if (activity != null)
            {
                activities.Add(activity);
            }
        }

        if (!options.CurrentValue.Enabled || activities.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
            context.Database.SetCommandTimeout(5);
            var userIds = activities.Select(activity => activity.UserId).Distinct().ToArray();
            var existingUsers = await context.Users.Where(user => userIds.Contains(user.Id) && !user.IsDeleted)
                .Select(user => user.Id).ToListAsync(cancellationToken);

            activities.RemoveAll(activity => !existingUsers.Contains(activity.UserId));
            var organizationIds = activities.Where(activity => activity.OrganizationId.HasValue)
                .Select(activity => activity.OrganizationId!.Value).Distinct().ToArray();

            if (organizationIds.Length > 0)
            {
                var existingOrganizations = await context.Organizations.Where(organization => organizationIds.Contains(organization.Id))
                    .Select(organization => organization.Id).ToListAsync(cancellationToken);

                foreach (var activity in activities)
                {
                    if (activity.OrganizationId.HasValue && !existingOrganizations.Contains(activity.OrganizationId.Value))
                    {
                        activity.OrganizationId = null;
                    }
                }
            }

            context.Set<UserActivityEvent>().AddRange(activities);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or System.Data.Common.DbException
            or Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException or TimeoutException)
        {
            logger.LogWarning(exception, "Не удалось сохранить пачку активности ({Count} событий). Пачка пропущена.", activities.Count);
        }
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
            context.Database.SetCommandTimeout(5);
            var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(options.CurrentValue.RetentionDays, 1, 365));
            var expired = context.Set<UserActivityEvent>().Where(activity => activity.ReceivedAt < cutoff)
                .OrderBy(activity => activity.ReceivedAt).Take(2000);

            return await expired.ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException
            or Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException or TimeoutException)
        {
            logger.LogWarning(exception, "Не удалось очистить устаревшую историю активности.");

            return 0;
        }
    }
}
