using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Notifications;
using OpenCRM.Modules.Promotions.Contracts;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Предоставляет оплаченный доступ однократно в транзакции с отметкой применения покупки. </summary>
internal sealed class SubscriptionPurchaseApplicator(
    OpenCRMContext context,
    IPromotionPaymentCoordinator promotions,
    TimeProvider clock,
    ILogger<SubscriptionPurchaseApplicator> logger)
{
    public async Task ApplyPendingAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await context.SubscriptionPurchases.AsNoTracking()
            .Where(item => item.ProviderStatus == "Succeeded" && item.AppliedAt == null
                && item.RefundedAmountMinor == 0 && item.ApplicationStatus == SubscriptionPurchaseStatuses.ActivationPending
                && item.NextApplicationAttemptAt <= now)
            .OrderBy(item => item.CreatedAt).Select(item => item.Id).Take(25).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            try
            {
                await this.ApplyAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                SubscriptionPurchaseDiagnostics.Failure(logger, "access_application", id, exception);
                await this.RecordFailureAsync(id, cancellationToken);
            }
        }
    }

    internal async Task ApplyAsync(Guid purchaseId, CancellationToken cancellationToken = default)
    {
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var key = await context.SubscriptionPurchases.AsNoTracking().Where(item => item.Id == purchaseId)
                .Select(item => item.BeneficiaryKey).SingleAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({key}))", cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + purchaseId.ToString("N")}))", cancellationToken);
            var purchase = await context.SubscriptionPurchases.Include(item => item.Payment).Include(item => item.CreationOperation)
                .SingleAsync(item => item.Id == purchaseId, cancellationToken);
            if (purchase.AppliedAt.HasValue || purchase.ProviderStatus != "Succeeded"
                || purchase.Payment.PaidAt == null || purchase.RefundedAmountMinor != 0
                || purchase.ApplicationStatus != SubscriptionPurchaseStatuses.ActivationPending)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var pendingRefund = await context.SubscriptionPaymentCommands.AnyAsync(command => command.PurchaseId == purchase.Id
                && command.Kind == "refund" && !context.SubscriptionPaymentAdminActions.Any(action => action.Id == command.Id
                    && (action.Status == "succeeded" || action.Status == "failed")), cancellationToken);
            if (pendingRefund)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            purchase.ApplicationAttempts++;
            var plan = SubscriptionPlanSnapshot.Restore(purchase.PlanSnapshot);
            if (plan.Id != purchase.CreationOperation.SubscriptionPlanId)
            {
                throw new InvalidOperationException("Снимок не соответствует оплаченному тарифу.");
            }

            var extension = purchase.CreationOperation.OperationType.Contains("extension", StringComparison.Ordinal);
            string? failure;
            if (purchase.OrganizationId.HasValue)
            {
                failure = await this.ApplyOrganizationAsync(purchase, plan, extension, now, cancellationToken);
            }
            else
            {
                failure = await this.ApplyPersonalAsync(purchase, plan, extension, now, cancellationToken);
            }

            if (failure != null)
            {
                purchase.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
                purchase.LastErrorCode = failure;
                purchase.NextApplicationAttemptAt = now.AddMinutes(5);
                SubscriptionPurchaseDiagnostics.Add(context, purchase, "access_application", failure, now);
                logger.LogError("Оплаченная покупка ожидает разбора доступа. PurchaseId={PurchaseId}, Code={Code}, Attempt={Attempt}",
                    purchase.Id, failure, purchase.ApplicationAttempts);
            }
            else
            {
                if (purchase.OrganizationId.HasValue)
                {
                    await promotions.ConsumeAsync(purchase.Payment, purchase.OrganizationId.Value);
                }

                purchase.AppliedAt = now;
                purchase.IsOpen = false;
                purchase.ApplicationStatus = purchase.RefundedAmountMinor > 0
                    ? SubscriptionPurchaseStatuses.NeedsAttention : SubscriptionPurchaseStatuses.Applied;
                purchase.LastErrorCode = purchase.RefundedAmountMinor > 0 ? "refund_requires_access_review" : null;
                purchase.Payment.PaymentStatus = "completed";
                purchase.Payment.StatusUpdatedAt = now;
                SubscriptionPurchaseDiagnostics.Add(context, purchase, "access_application", "applied", now);
                var userEmail = await context.Users.Where(item => item.Id == purchase.UserId)
                    .Select(item => item.Email).SingleOrDefaultAsync(cancellationToken);
                TelegramAdminNotificationPublisher.AddPurchase(context, purchase.UserId, userEmail,
                    plan.Name, purchase.Payment.Amount, purchase.Payment.ExternalPaymentId!);
                logger.LogInformation("Оплаченный доступ предоставлен. PurchaseId={PurchaseId}, PlanId={PlanId}, GrantedUntil={GrantedUntil}",
                    purchase.Id, plan.Id, purchase.GrantedUntil);
            }

            purchase.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task<string?> ApplyOrganizationAsync(SubscriptionPurchase purchase, SubscriptionPlan plan,
        bool extension, DateTime now, CancellationToken cancellationToken)
    {
        var organizations = await context.Organizations.FromSqlInterpolated(
            $"SELECT * FROM \"Organizations\" WHERE \"Id\" = {purchase.OrganizationId!.Value} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        var organization = organizations.SingleOrDefault();
        if (organization == null || !organization.IsActive || organization.IsDeleted || organization.IsArchived)
        {
            return "beneficiary_organization_unavailable";
        }

        var current = await context.OrganizationSubscriptions.Include(item => item.SubscriptionPlan)
            .Where(item => item.OrganizationId == organization.Id && item.EndDate >= now)
            .OrderByDescending(item => item.EndDate).FirstOrDefaultAsync(cancellationToken);
        if (extension && current != null && current.SubscriptionPlanId != plan.Id)
        {
            return "subscription_changed_after_checkout";
        }

        if (!extension && current != null && current.SourceCode != "promo"
            && !current.SubscriptionPlan.IsDemoPlan && !current.SubscriptionPlan.IsDefaultPlan)
        {
            return "paid_subscription_changed_after_checkout";
        }

        var baseDate = current != null && current.EndDate > now ? current.EndDate : now;
        if (extension && current != null)
        {
            purchase.GrantedFrom = baseDate;
            purchase.GrantedUntil = baseDate.AddDays(plan.DurationDays);
            current.EndDate = purchase.GrantedUntil.Value;
            current.SubscriptionPurchaseId = purchase.Id;
            current.SourceCode = "paid";
        }
        else
        {
            if (current != null)
            {
                current.EndDate = now;
            }

            purchase.GrantedFrom = now;
            purchase.GrantedUntil = baseDate.AddDays(plan.DurationDays);
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                OrganizationId = organization.Id,
                SubscriptionPlanId = plan.Id,
                SubscriptionPurchaseId = purchase.Id,
                SourceCode = "paid",
                StartDate = now,
                EndDate = purchase.GrantedUntil.Value
            });
        }

        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            OrganizationId = organization.Id,
            ActorUserId = purchase.UserId,
            EventType = extension ? "subscription.extended" : "subscription.purchased",
            Title = extension ? "Тариф продлён после оплаты" : "Тариф оплачен",
            Description = $"Покупка {purchase.Id:D}; срок до {purchase.GrantedUntil:O}",
            TargetType = "subscription_purchase",
            TargetId = purchase.Id.ToString("D"),
            CreatedAt = now
        });

        return null;
    }

    private async Task<string?> ApplyPersonalAsync(SubscriptionPurchase purchase, SubscriptionPlan plan,
        bool extension, DateTime now, CancellationToken cancellationToken)
    {
        var users = await context.ApplicationUsers.FromSqlInterpolated(
            $"SELECT * FROM identity.\"AspNetUsers\" WHERE \"Id\" = {purchase.UserId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (users.Count != 1 || users[0].IsDeleted)
        {
            return "beneficiary_user_unavailable";
        }

        var subscriptions = await context.UserSubscriptions.Include(item => item.SubscriptionPlan)
            .Where(item => item.UserId == purchase.UserId).OrderByDescending(item => item.EndDate).ToListAsync(cancellationToken);
        var active = subscriptions.Where(item => item.EndDate >= now).ToList();
        if (active.Count > 1)
        {
            return "ambiguous_active_subscription";
        }

        var current = active.SingleOrDefault();
        if (current != null && (extension && current.SubscriptionPlanId != plan.Id
            || !extension && current.SubscriptionPlan.Order > plan.Order))
        {
            return "subscription_changed_after_checkout";
        }

        var baseDate = current != null && current.EndDate > now ? current.EndDate : now;
        var subscription = current ?? subscriptions.FirstOrDefault();
        if (subscription == null)
        {
            subscription = new UserSubscription { UserId = purchase.UserId };
            context.UserSubscriptions.Add(subscription);
        }

        purchase.GrantedFrom = extension ? baseDate : now;
        purchase.GrantedUntil = baseDate.AddDays(plan.DurationDays);
        subscription.SubscriptionPlanId = plan.Id;
        subscription.SubscriptionPurchaseId = purchase.Id;
        subscription.SourceCode = "paid";
        subscription.AutoRenewal = false;
        if (!extension || current == null)
        {
            subscription.StartDate = now;
        }

        subscription.EndDate = purchase.GrantedUntil.Value;
        context.UserSubscriptionHistory.Add(new UserSubscriptionHistory
        {
            UserId = purchase.UserId,
            SubscriptionPlanId = plan.Id,
            SourceCode = "paid",
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            Status = extension ? "extended" : "created",
            CreatedAt = now,
            Comment = $"Оплаченная покупка {purchase.Id:D}"
        });

        return null;
    }

    private async Task RecordFailureAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + id.ToString("N")}))", cancellationToken);
                var purchase = await context.SubscriptionPurchases.SingleAsync(item => item.Id == id, cancellationToken);
                if (purchase.AppliedAt.HasValue || purchase.RefundedAmountMinor != 0
                    || purchase.ApplicationStatus != SubscriptionPurchaseStatuses.ActivationPending)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }

                var now = clock.GetUtcNow().UtcDateTime;
                purchase.ApplicationAttempts++;
                purchase.LastErrorCode = "access_transaction_failed";
                purchase.NextApplicationAttemptAt = now.AddMinutes(1);
                SubscriptionPurchaseDiagnostics.Add(context, purchase, "access_application", "access_transaction_failed", now);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (Exception exception)
        {
            SubscriptionPurchaseDiagnostics.Failure(logger, "access_failure_journal", id, exception);
        }
    }
}
