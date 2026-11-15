using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using System.Data;

namespace OpenCRM.Modules.Billing;

/// <summary> Выполнение мутаций подписок с аудитом. </summary>
internal sealed class AdminSubscriptionMutation(OpenCRMContext opencrmContext)
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Выполняет операцию изменения подписки. </summary>
    public async Task<AdminBillingSubscriptionOperationDto> ExecuteAsync(
        Func<Task<AdminBillingSubscriptionOperationDto>> operation)
    {
        var isRetry = false;
        var strategy = this._opencrmContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            if (isRetry)
            {
                this._opencrmContext.ChangeTracker.Clear();
            }

            isRetry = true;
            using var transaction = await this._opencrmContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var result = await operation();
            if (result.Succeeded)
            {
                await transaction.CommitAsync();
            }

            return result;
        });
    }

    /// <summary> Добавляет запись аудита изменения подписки. </summary>
    public void AddAudit(
        string scope,
        int subscriptionId,
        string clientId,
        string action,
        DateTime? previousEndDate,
        DateTime? newEndDate,
        string adminUserId,
        string reason,
        DateTime changedAt)
    {
        this._opencrmContext.AdminSubscriptionAudits.Add(new AdminSubscriptionAudit
        {
            Scope = scope,
            SubscriptionId = subscriptionId,
            ClientId = clientId,
            Action = action,
            PreviousEndDate = previousEndDate,
            NewEndDate = newEndDate,
            ChangedByUserId = adminUserId,
            Reason = reason,
            ChangedAt = changedAt
        });
    }
}
