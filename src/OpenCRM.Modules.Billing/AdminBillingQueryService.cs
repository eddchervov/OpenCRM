using System.Globalization;
using Helpers.Payments;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Domain;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис запросов административной панели биллинга. </summary>
internal sealed class AdminBillingQueryService(OpenCRMContext opencrmContext) : IAdminBillingQueryService
{
    /// <summary> Статусы успешных платежей. </summary>
    private static readonly string[] s_successPaymentStatuses = ["completed", "succeeded", "success", "paid", "done"];

    /// <summary> Статусы ожидающих платежей. </summary>
    private static readonly string[] s_pendingPaymentStatuses =
        ["pending", "processing", "waiting_for_capture", "paid_waiting_confirmation"];

    /// <summary> Все известные статусы платежей. </summary>
    private static readonly string[] s_knownPaymentStatuses =
    [
        "completed", "succeeded", "success", "paid", "done", "pending", "processing", "waiting_for_capture",
        "paid_waiting_confirmation", "canceled", "refunded", "failed", "error"
    ];

    /// <summary> Проекция строки подписки организации. </summary>
    private static readonly System.Linq.Expressions.Expression<Func<OrganizationSubscription, OrganizationSubscriptionRow>>
        s_organizationSubscriptionProjection = subscription => new OrganizationSubscriptionRow(
            subscription.Id,
            subscription.OrganizationId,
            subscription.Organization.PrimaryScenarioCode,
            subscription.Organization.Name,
            subscription.Organization.OwnerUser.Email ??
                subscription.Organization.OwnerUser.PhoneNumber ??
                subscription.Organization.OwnerUserId,
            subscription.Organization.OwnerUserId,
            subscription.SubscriptionPlanId,
            subscription.SubscriptionPlan.Name,
            subscription.SubscriptionPlan.PlanTypeCode,
            subscription.SubscriptionPlan.IsDemoPlan,
            subscription.SourceCode,
            subscription.StartDate,
            subscription.EndDate,
            subscription.AutoRenewal);

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает сводку внимания панели биллинга. </summary>
    public async Task<AdminBillingAttentionDto> GetAttentionAsync(int? periodDays = null)
    {
        var normalizedPeriodDays = periodDays is null or 1 or 7 or 30 ? periodDays : 1;
        var utcNow = DateTime.UtcNow;
        var utcToday = utcNow.Date;

        var periodStart = normalizedPeriodDays switch
        {
            1 => utcToday,
            7 => utcToday.AddDays(-6),
            30 => utcToday.AddDays(-29),
            _ => (DateTime?)null
        };

        var periodEnd = periodStart.HasValue ? utcToday.AddDays(1) : (DateTime?)null;
        var overdueBefore = utcNow.AddDays(-1);

        var missingReceiptPayments = this._opencrmContext.SubscriptionPayments.AsNoTracking()
            .Where(payment => payment.PaidAt != null
                && payment.NpdReceiptIssuedAt == null
                && payment.RefundedAmount < payment.Amount
                && (payment.PaymentStatus == "completed" || payment.PaymentStatus == "failed"));

        if (periodStart.HasValue && periodEnd.HasValue)
        {
            missingReceiptPayments = missingReceiptPayments.Where(payment =>
                payment.PaidAt >= periodStart.Value && payment.PaidAt < periodEnd.Value);
        }

        var missingReceiptCount = await missingReceiptPayments.CountAsync();
        var overdueReceiptCount = await missingReceiptPayments.CountAsync(payment => payment.PaidAt <= overdueBefore);

        var failedActivationCount = await this._opencrmContext.SubscriptionPayments.AsNoTracking()
            .CountAsync(payment => payment.PaidAt != null && payment.PaymentStatus == "failed"
                && payment.RefundedAmount < payment.Amount);

        return new AdminBillingAttentionDto
        {
            PeriodDays = normalizedPeriodDays,
            MissingReceiptCount = missingReceiptCount,
            OverdueReceiptCount = overdueReceiptCount,
            FailedActivationCount = failedActivationCount
        };
    }

    /// <summary> Возвращает список покупок. </summary>
    public async Task<IReadOnlyList<AdminBillingPurchaseDto>> GetPurchasesAsync()
    {
        var purchases = await this._opencrmContext.SubscriptionPayments
            .AsNoTracking()
            .Where(payment => payment.PaidAt != null
                && (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                    || payment.PaymentStatus == "failed"))
            .OrderByDescending(payment => payment.PaidAt ?? payment.StatusUpdatedAt ?? payment.CreatedAt)
            .Select(payment => new AdminBillingPurchaseDto
            {
                PaymentId = payment.Id,
                UserId = payment.UserId,
                User = payment.User.Email ?? payment.User.PhoneNumber ?? payment.UserId,
                UserEmail = payment.User.Email,
                UserPhone = payment.User.PhoneNumber,
                BuyerType = payment.BuyerTypeCode,
                BuyerName = payment.BuyerName,
                BuyerInn = payment.BuyerInn,
                PlanName = payment.SubscriptionPlan.Name,
                PaymentStatus = payment.PaymentStatus,
                Amount = payment.Amount,
                RefundedAmount = payment.RefundedAmount,
                PaidAt = payment.PaidAt ?? payment.StatusUpdatedAt ?? payment.CreatedAt,
                NpdReceiptIssued = payment.NpdReceiptIssuedAt != null,
                NpdReceiptIssuedAt = payment.NpdReceiptIssuedAt,
                NpdReceiptMarkedBy = payment.NpdReceiptMarkedByUserId,
                NpdReceiptReason = payment.NpdReceiptReason,
                NpdReceiptUrl = payment.NpdReceiptUrl,
                NpdReceiptComment = payment.NpdReceiptComment,
                NpdReceiptFileName = payment.NpdReceiptFileName,
                NpdReceiptFileUrl = payment.NpdReceiptFilePath == null
                    ? null
                    : "/api/v1/admin/billing/payments/" + payment.Id + "/npd-receipt/file"
            })
            .ToListAsync();

        var paymentIds = purchases.Select(item => item.PaymentId).ToArray();
        var deliveryReferences = await this._opencrmContext.SubscriptionPayments.AsNoTracking()
            .Where(item => paymentIds.Contains(item.Id) && item.NpdReceiptEmailMessageId != null)
            .Select(item => new { item.Id, item.NpdReceiptEmailMessageId })
            .ToListAsync();
        var messageIds = deliveryReferences.Select(item => item.NpdReceiptEmailMessageId!.Value).ToArray();
        var messages = await this._opencrmContext.EmailMessages.AsNoTracking()
            .Where(item => messageIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id);
        var purchasesById = purchases.ToDictionary(item => item.PaymentId);

        foreach (var reference in deliveryReferences)
        {
            if (!messages.TryGetValue(reference.NpdReceiptEmailMessageId!.Value, out var message))
            {
                continue;
            }

            var purchase = purchasesById[reference.Id];
            purchase.NpdReceiptDeliveryStatus = message.StatusCode is EmailMessageStatusCodes.Failed
                or EmailMessageStatusCodes.HardBounced or EmailMessageStatusCodes.SoftBounced
                or EmailMessageStatusCodes.Spam or EmailMessageStatusCodes.SpamBlock
                or EmailMessageStatusCodes.Unsubscribed
                ? "failed"
                : message.StatusCode is EmailMessageStatusCodes.Delivered
                    or EmailMessageStatusCodes.Opened or EmailMessageStatusCodes.Clicked
                    ? "delivered"
                    : message.StatusCode == EmailMessageStatusCodes.Simulated
                        ? "simulated"
                        : message.SentAt.HasValue ? "sent" : "queued";
            purchase.NpdReceiptSentAt = message.SentAt;
            purchase.NpdReceiptSentTo = message.ToEmail;
            purchase.NpdReceiptSendError = message.ErrorMessage;
        }

        return purchases;
    }

    /// <summary> Возвращает список доступных тарифов. </summary>
    public async Task<IReadOnlyList<AdminBillingAvailablePlanDto>> GetAvailablePlansAsync()
    {
        return await this._opencrmContext.SubscriptionPlans
            .AsNoTracking()
            .Where(plan => plan.IsActive && !plan.IsDemoPlan && !plan.IsDefaultPlan
                && plan.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
            .OrderBy(plan => plan.Order)
            .ThenBy(plan => plan.Name)
            .Select(plan => new AdminBillingAvailablePlanDto
            {
                Id = plan.Id,
                Name = plan.Name,
                PlanTypeCode = plan.PlanTypeCode,
                DurationDays = plan.DurationDays
            })
            .ToListAsync();
    }

    /// <summary> Возвращает платёж по идентификатору. </summary>
    public async Task<AdminBillingPaymentDto?> GetPaymentAsync(int paymentId)
    {
        var payment = await this.GetPaymentRows(paymentId).SingleOrDefaultAsync();
        if (payment == null)
        {
            return null;
        }

        var purchase = await this._opencrmContext.SubscriptionPurchases.AsNoTracking()
            .Where(item => item.SubscriptionPaymentId == paymentId)
            .SingleOrDefaultAsync();
        var organizationName = purchase?.OrganizationId is Guid organizationId
            ? await this._opencrmContext.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
                .Select(item => item.Name).SingleOrDefaultAsync()
            : null;
        IReadOnlyList<AdminBillingPaymentEventDto> providerEvents = [];
        if (purchase != null)
        {
            providerEvents = await this._opencrmContext.SubscriptionPaymentEvents.AsNoTracking()
                .Where(item => item.PurchaseId == purchase.Id)
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .Take(20)
                .Select(item => new AdminBillingPaymentEventDto
                {
                    Id = item.Id,
                    MessageId = item.MessageId,
                    PurchaseId = item.PurchaseId,
                    CommandId = item.CommandId,
                    ProviderOrderId = item.ProviderOrderId,
                    ProviderVersion = item.ProviderVersion,
                    Stage = item.Kind,
                    Code = item.Code,
                    CreatedAt = DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc),
                    ProcessedAt = item.ProcessedAt.HasValue
                        ? DateTime.SpecifyKind(item.ProcessedAt.Value, DateTimeKind.Utc)
                        : null,
                    AmountMinor = item.AmountMinor,
                    RefundedAmountMinor = item.RefundedAmountMinor,
                    Currency = purchase.Currency
                })
                .ToListAsync();
        }

        return AdminBillingQueryService.ToPaymentDto(payment, purchase?.Id, purchase?.ApplicationStatus,
            purchase?.LastErrorCode, providerEvents, purchase, organizationName);
    }

    /// <summary> Формирует запрос строк платежей с фильтрами. </summary>
    private IQueryable<PaymentRow> GetPaymentRows(
        int? paymentId = null,
        bool unbounded = false,
        IQueryable<SubscriptionPayment>? paymentQuery = null)
    {
        var query = paymentQuery ?? this._opencrmContext.SubscriptionPayments.AsNoTracking();
        if (paymentId.HasValue)
        {
            query = query.Where(payment => payment.Id == paymentId.Value);
        }
        else if (!unbounded)
        {
            query = query.OrderByDescending(payment => payment.CreatedAt).Take(300);
        }

        return query
            .Select(payment => new PaymentRow(
                payment.Id,
                payment.UserId,
                payment.User.Email ?? payment.User.PhoneNumber ?? payment.UserId,
                payment.User.Email,
                payment.User.PhoneNumber,
                payment.SubscriptionPlanId,
                payment.PaymentProviderCode,
                payment.SubscriptionPlan.Name,
                payment.SubscriptionPlan.PlanTypeCode,
                payment.SubscriptionPlan.DurationDays,
                payment.Amount,
                payment.RefundedAmount,
                payment.PaidAt,
                payment.OriginalAmount,
                payment.PromoDiscountAmount,
                payment.PromoCodeSnapshot,
                payment.CreatedAt,
                payment.StatusUpdatedAt,
                payment.PaymentMethod,
                payment.PaymentStatus,
                payment.TransactionId,
                payment.ExternalPaymentId,
                payment.PaymentLink));
    }

    /// <summary> Преобразует строку платежа в DTO. </summary>
    private static AdminBillingPaymentDto ToPaymentDto(
        PaymentRow payment,
        Guid? purchaseId = null,
        string? applicationStatus = null,
        string? lastErrorCode = null,
        IReadOnlyList<AdminBillingPaymentEventDto>? providerEvents = null,
        SubscriptionPurchase? purchase = null,
        string? organizationName = null) => new()
    {
        Id = payment.Id,
        UserId = payment.UserId,
        User = payment.User,
        UserEmail = payment.UserEmail,
        UserPhone = payment.UserPhone,
        SubscriptionPlanId = payment.SubscriptionPlanId,
        PaymentProviderCode = payment.PaymentProviderCode,
        PurchaseId = purchaseId,
        ApplicationStatus = applicationStatus,
        LastErrorCode = lastErrorCode,
        ProviderEvents = providerEvents ?? [],
        PlanName = payment.PlanName,
        PlanTypeCode = payment.PlanTypeCode,
        PlanDurationDays = payment.PlanDurationDays,
        Amount = payment.Amount,
        RefundedAmount = payment.RefundedAmount,
        PaidAt = payment.PaidAt,
        ProviderStatus = purchase?.ProviderStatus,
        AppliedAt = purchase?.AppliedAt,
        GrantedFrom = purchase?.GrantedFrom,
        GrantedUntil = purchase?.GrantedUntil,
        IsTest = purchase?.IsTest,
        OrganizationId = purchase?.OrganizationId,
        OrganizationName = organizationName,
        OriginalAmount = payment.OriginalAmount,
        PromoDiscountAmount = payment.PromoDiscountAmount,
        PromoCodeSnapshot = payment.PromoCodeSnapshot,
        CreatedAt = payment.CreatedAt,
        StatusUpdatedAt = payment.StatusUpdatedAt,
        PaymentMethod = payment.PaymentMethod,
        PaymentMethodName = AdminPaymentDisplay.ResolvePaymentMethodName(payment.PaymentMethod),
        PaymentStatus = payment.PaymentStatus,
        StatusGroup = applicationStatus == SubscriptionPurchaseStatuses.NeedsAttention
            ? "problem"
            : AdminPaymentDisplay.ResolvePaymentStatusGroup(payment.PaymentStatus),
        StatusName = applicationStatus == SubscriptionPurchaseStatuses.NeedsAttention
            ? "Требует проверки"
            : AdminPaymentDisplay.ResolvePaymentStatusName(payment.PaymentStatus),
        IsProblem = applicationStatus == SubscriptionPurchaseStatuses.NeedsAttention
            || AdminPaymentDisplay.ResolvePaymentStatusGroup(payment.PaymentStatus) == "problem",
        TransactionId = payment.TransactionId,
        ExternalPaymentId = payment.ExternalPaymentId,
        PaymentLink = payment.PaymentLink,
        HasExternalId = !string.IsNullOrWhiteSpace(payment.ExternalPaymentId),
        HasPaymentLink = !string.IsNullOrWhiteSpace(payment.PaymentLink)
    };

    /// <summary> Возвращает сводку административной панели биллинга. </summary>
    public async Task<AdminBillingDto> GetAsync()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);
        var thirtyDaysAgo = today.AddDays(-29);
        var sevenDaysAhead = today.AddDays(7);

        var (dailyPayments, statusBreakdown, rawStatusBreakdown) =
            await this.LoadPaymentChartsAsync(thirtyDaysAgo, today, tomorrow);

        var paymentsRaw = await this.GetPaymentRows().ToListAsync();
        var purchaseSummaries = await this.LoadPaymentPurchaseSummariesAsync(paymentsRaw.Select(item => item.Id));

        var payments = paymentsRaw
            .Select(payment => purchaseSummaries.TryGetValue(payment.Id, out var purchase)
                ? AdminBillingQueryService.ToPaymentDto(payment, purchase.Id, purchase.ApplicationStatus, purchase.LastErrorCode)
                : AdminBillingQueryService.ToPaymentDto(payment))
            .ToList();

        var userSubscriptions = await this.LoadUserSubscriptionsAsync(now, sevenDaysAhead);

        var organizationSubscriptions = await this.LoadOrganizationSubscriptionsAsync(now, sevenDaysAhead);

        var planRevenue = await this._opencrmContext.SubscriptionPayments
            .AsNoTracking()
            .Where(payment => payment.PaidAt >= thirtyDaysAgo
                && (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                    || payment.PaymentStatus == "failed"))
            .GroupBy(payment => new
            {
                payment.SubscriptionPlanId,
                payment.SubscriptionPlan.Name
            })
            .Select(group => new AdminBillingPlanRevenueDto
            {
                PlanId = group.Key.SubscriptionPlanId,
                Plan = group.Key.Name,
                Count = group.Count(),
                Amount = group.Sum(payment => payment.Amount - (payment.PaymentStatus == "refunded"
                    && payment.RefundedAmount == 0 ? payment.Amount : payment.RefundedAmount))
            })
            .OrderByDescending(row => row.Amount)
            .Take(8)
            .ToListAsync();

        var kpis = await this.LoadKpisAsync(now, sevenDaysAhead, dailyPayments, statusBreakdown);

        return new AdminBillingDto
        {
            GeneratedAt = now,
            Kpis = kpis,
            Charts = new AdminBillingChartsDto
            {
                DailyPayments = dailyPayments,
                StatusBreakdown = statusBreakdown,
                PlanRevenue = planRevenue
            },
            RawStatusBreakdown = rawStatusBreakdown,
            Payments = payments,
            Subscriptions = new AdminBillingSubscriptionsDto
            {
                Users = userSubscriptions,
                Organizations = organizationSubscriptions
            }
        };
    }

    /// <summary> Возвращает страницу платежей. </summary>
    public async Task<AdminBillingPaymentsPageDto> GetPaymentsPageAsync(AdminBillingPageQuery query)
    {
        var now = DateTime.UtcNow;
        var periodDays = AdminBillingQueryService.NormalizePeriodDays(query.PeriodDays);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var payments = this._opencrmContext.SubscriptionPayments.AsNoTracking();

        if (periodDays > 0)
        {
            var periodStart = now.Date.AddDays(-(periodDays - 1));
            var periodEnd = now.Date.AddDays(1);
            payments = payments.Where(payment => payment.CreatedAt >= periodStart && payment.CreatedAt < periodEnd);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var searchText = search.ToLowerInvariant();
            var searchPaymentId = int.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var paymentId)
                ? paymentId
                : (int?)null;
            payments = payments.Where(payment =>
                searchPaymentId.HasValue && payment.Id == searchPaymentId.Value
                || payment.User.Email != null && payment.User.Email.ToLower().Contains(searchText)
                || payment.User.PhoneNumber != null && payment.User.PhoneNumber.ToLower().Contains(searchText)
                || payment.UserId.ToLower().Contains(searchText)
                || payment.SubscriptionPlan.Name.ToLower().Contains(searchText)
                || payment.PaymentStatus.ToLower().Contains(searchText)
                || payment.TransactionId != null && payment.TransactionId.ToLower().Contains(searchText)
                || payment.ExternalPaymentId != null && payment.ExternalPaymentId.ToLower().Contains(searchText));
        }

        payments = this.FilterPaymentStatus(payments, query.Status);

        var totalCount = await payments.CountAsync();
        var pageSizeCount = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
        var page = Math.Min(Math.Max(1, query.Page), pageSizeCount);
        var sortedPayments = AdminBillingQueryService.SortPayments(payments, query.Sort, query.Descending);
        var paymentRows = await this.GetPaymentRows(
                unbounded: true,
                paymentQuery: sortedPayments.Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync();
        var purchaseSummaries = await this.LoadPaymentPurchaseSummariesAsync(paymentRows.Select(item => item.Id));

        return new AdminBillingPaymentsPageDto
        {
            Items = paymentRows.Select(payment => purchaseSummaries.TryGetValue(payment.Id, out var purchase)
                ? AdminBillingQueryService.ToPaymentDto(payment, purchase.Id, purchase.ApplicationStatus, purchase.LastErrorCode)
                : AdminBillingQueryService.ToPaymentDto(payment)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private async Task<Dictionary<int, PaymentPurchaseSummary>> LoadPaymentPurchaseSummariesAsync(IEnumerable<int> paymentIds)
    {
        var ids = paymentIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await this._opencrmContext.SubscriptionPurchases.AsNoTracking()
            .Where(purchase => ids.Contains(purchase.SubscriptionPaymentId))
            .ToDictionaryAsync(purchase => purchase.SubscriptionPaymentId,
                purchase => new PaymentPurchaseSummary(purchase.Id, purchase.ApplicationStatus, purchase.LastErrorCode));
    }

    /// <summary> Возвращает страницу подписок организаций. </summary>
    public async Task<AdminBillingOrganizationSubscriptionsPageDto> GetOrganizationSubscriptionsPageAsync(
        AdminBillingPageQuery query)
    {
        var now = DateTime.UtcNow;
        var sevenDaysAhead = now.AddDays(7);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var subscriptions = this._opencrmContext.OrganizationSubscriptions.AsNoTracking();

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var searchText = search.ToLowerInvariant();
            var searchSubscriptionId = int.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var subscriptionId)
                ? subscriptionId
                : (int?)null;
            subscriptions = subscriptions.Where(subscription =>
                searchSubscriptionId.HasValue && subscription.Id == searchSubscriptionId.Value
                || subscription.Organization.Name.ToLower().Contains(searchText)
                || (subscription.Organization.OwnerUser.Email ?? string.Empty).ToLower().Contains(searchText)
                || (subscription.Organization.OwnerUser.PhoneNumber ?? string.Empty).ToLower().Contains(searchText)
                || subscription.Organization.OwnerUserId.ToLower().Contains(searchText)
                || subscription.SubscriptionPlan.Name.ToLower().Contains(searchText)
                || subscription.SourceCode.ToLower().Contains(searchText));
        }

        var status = query.Status?.Trim().ToLowerInvariant();
        if (status == "active")
        {
            subscriptions = subscriptions.Where(subscription => subscription.StartDate <= now && subscription.EndDate >= now);
        }
        else if (status is "expired" or "completed")
        {
            subscriptions = subscriptions.Where(subscription => subscription.EndDate < now);
        }
        else if (status == "scheduled")
        {
            subscriptions = subscriptions.Where(subscription => subscription.StartDate > now);
        }
        else if (status == "inactive")
        {
            subscriptions = subscriptions.Where(subscription => subscription.StartDate > now || subscription.EndDate < now);
        }

        var totalCount = await subscriptions.CountAsync();
        var pageSizeCount = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
        var page = Math.Min(Math.Max(1, query.Page), pageSizeCount);
        var subscriptionRows = await AdminBillingQueryService.SortOrganizationSubscriptions(
                subscriptions, query.Sort, query.Descending, now)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(AdminBillingQueryService.s_organizationSubscriptionProjection)
            .ToListAsync();

        return new AdminBillingOrganizationSubscriptionsPageDto
        {
            Items = subscriptionRows
                .Select(subscription => AdminBillingQueryService.ToOrganizationSubscriptionDto(subscription, now, sevenDaysAhead))
                .ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary> Нормализует период в днях. </summary>
    private static int NormalizePeriodDays(int periodDays) => periodDays is 0 or 1 or 7 or 30 ? periodDays : 30;

    /// <summary> Фильтрует платежи по статусу. </summary>
    private IQueryable<SubscriptionPayment> FilterPaymentStatus(
        IQueryable<SubscriptionPayment> payments,
        string? status)
    {
        var normalizedStatus = status?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedStatus) || normalizedStatus == "all")
        {
            return payments;
        }

        if (normalizedStatus is "activationfailed" or "activation-failed")
        {
            return payments.Where(payment => payment.PaidAt != null
                && payment.PaymentStatus.ToLower() == "failed"
                && payment.RefundedAmount < payment.Amount);
        }

        var purchasesNeedingAttention = this._opencrmContext.SubscriptionPurchases.AsNoTracking()
            .Where(purchase => purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention)
            .Select(purchase => purchase.SubscriptionPaymentId);

        if (normalizedStatus is "success" or "pending" or "canceled" or "refunded" or "unknown")
        {
            payments = payments.Where(payment => !purchasesNeedingAttention.Contains(payment.Id));
        }

        if (normalizedStatus == "success")
        {
            return payments.Where(payment => s_successPaymentStatuses.Contains(payment.PaymentStatus.ToLower().Trim()));
        }

        if (normalizedStatus == "pending")
        {
            return payments.Where(payment => s_pendingPaymentStatuses.Contains(payment.PaymentStatus.ToLower().Trim()));
        }

        if (normalizedStatus == "canceled")
        {
            return payments.Where(payment => payment.PaymentStatus.ToLower().Contains("cancel"));
        }

        if (normalizedStatus == "refunded")
        {
            return payments.Where(payment => payment.PaymentStatus.ToLower().Contains("refund"));
        }

        if (normalizedStatus is "problem" or "failed")
        {
            return payments.Where(payment => payment.PaymentStatus.ToLower().Contains("fail")
                || payment.PaymentStatus.ToLower().Contains("error")
                || purchasesNeedingAttention.Contains(payment.Id));
        }

        if (normalizedStatus == "unknown")
        {
            return payments.Where(payment => payment.PaymentStatus == null
                || payment.PaymentStatus.Trim().Length == 0
                || !s_knownPaymentStatuses.Contains(payment.PaymentStatus.ToLower().Trim())
                    && !payment.PaymentStatus.ToLower().Contains("cancel")
                    && !payment.PaymentStatus.ToLower().Contains("refund")
                    && !payment.PaymentStatus.ToLower().Contains("fail")
                    && !payment.PaymentStatus.ToLower().Contains("error"));
        }

        return payments;
    }

    /// <summary> Сортирует платежи по указанному полю. </summary>
    private static IOrderedQueryable<SubscriptionPayment> SortPayments(
        IQueryable<SubscriptionPayment> payments,
        string? sort,
        bool descending)
    {
        var normalizedSort = sort?.Trim().ToLowerInvariant();
        if (descending)
        {
            return normalizedSort switch
            {
                "customer" => payments.OrderByDescending(payment =>
                    payment.User.Email ?? payment.User.PhoneNumber ?? payment.UserId).ThenBy(payment => payment.Id),
                "plan" => payments.OrderByDescending(payment => payment.SubscriptionPlan.Name).ThenBy(payment => payment.Id),
                "amount" => payments.OrderByDescending(payment => payment.Amount).ThenBy(payment => payment.Id),
                "status" => payments.OrderByDescending(payment => payment.PaymentStatus).ThenBy(payment => payment.Id),
                _ => payments.OrderByDescending(payment => payment.CreatedAt).ThenBy(payment => payment.Id)
            };
        }

        return normalizedSort switch
        {
            "customer" => payments.OrderBy(payment => payment.User.Email ?? payment.User.PhoneNumber ?? payment.UserId)
                .ThenBy(payment => payment.Id),
            "plan" => payments.OrderBy(payment => payment.SubscriptionPlan.Name).ThenBy(payment => payment.Id),
            "amount" => payments.OrderBy(payment => payment.Amount).ThenBy(payment => payment.Id),
            "status" => payments.OrderBy(payment => payment.PaymentStatus).ThenBy(payment => payment.Id),
            _ => payments.OrderBy(payment => payment.CreatedAt).ThenBy(payment => payment.Id)
        };
    }

    /// <summary> Сортирует подписки организаций по указанному полю. </summary>
    private static IOrderedQueryable<OrganizationSubscription> SortOrganizationSubscriptions(
        IQueryable<OrganizationSubscription> subscriptions,
        string? sort,
        bool descending,
        DateTime now)
    {
        var normalizedSort = sort?.Trim().ToLowerInvariant();
        if (descending)
        {
            return normalizedSort switch
            {
                "source" => subscriptions.OrderByDescending(subscription => subscription.SourceCode).ThenBy(subscription => subscription.Id),
                "end" or "expires" => subscriptions.OrderByDescending(subscription => subscription.EndDate).ThenBy(subscription => subscription.Id),
                "date" or "start" => subscriptions.OrderByDescending(subscription => subscription.StartDate).ThenBy(subscription => subscription.Id),
                "organization" or "customer" => subscriptions.OrderByDescending(subscription => subscription.Organization.Name).ThenBy(subscription => subscription.Id),
                "plan" => subscriptions.OrderByDescending(subscription => subscription.SubscriptionPlan.Name).ThenBy(subscription => subscription.Id),
                "status" => subscriptions.OrderByDescending(subscription => subscription.StartDate <= now && subscription.EndDate >= now)
                    .ThenBy(subscription => subscription.Id),
                _ => subscriptions.OrderByDescending(subscription => subscription.StartDate).ThenBy(subscription => subscription.Id)
            };
        }

        return normalizedSort switch
        {
            "source" => subscriptions.OrderBy(subscription => subscription.SourceCode).ThenBy(subscription => subscription.Id),
            "end" or "expires" => subscriptions.OrderBy(subscription => subscription.EndDate).ThenBy(subscription => subscription.Id),
            "date" or "start" => subscriptions.OrderBy(subscription => subscription.StartDate).ThenBy(subscription => subscription.Id),
            "organization" or "customer" => subscriptions.OrderBy(subscription => subscription.Organization.Name).ThenBy(subscription => subscription.Id),
            "plan" => subscriptions.OrderBy(subscription => subscription.SubscriptionPlan.Name).ThenBy(subscription => subscription.Id),
            "status" => subscriptions.OrderBy(subscription => subscription.StartDate <= now && subscription.EndDate >= now)
                .ThenBy(subscription => subscription.Id),
            _ => subscriptions.OrderBy(subscription => subscription.StartDate).ThenBy(subscription => subscription.Id)
        };
    }

    /// <summary> Загружает данные графиков платежей. </summary>
    private async Task<(
        IReadOnlyList<AdminBillingDailyMoneyDto> DailyPayments,
        IReadOnlyList<AdminBillingStatusGroupDto> StatusBreakdown,
        IReadOnlyList<AdminBillingRawStatusDto> RawStatusBreakdown)> LoadPaymentChartsAsync(
            DateTime thirtyDaysAgo, DateTime today, DateTime tomorrow)
    {
        var dailyPayments = await AdminBillingQueryService.BuildMoneySeriesAsync(
            thirtyDaysAgo,
            today,
            this._opencrmContext.SubscriptionPayments
                .AsNoTracking()
                .Where(payment => payment.PaidAt >= thirtyDaysAgo
                    && (payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                        || payment.PaymentStatus == "failed"))
                .GroupBy(payment => payment.PaidAt!.Value.Date)
                .Select(group => new DailyMoneyPoint(
                    group.Key,
                    group.Sum(payment => payment.Amount - (payment.PaymentStatus == "refunded"
                        && payment.RefundedAmount == 0 ? payment.Amount : payment.RefundedAmount)),
                    group.Count())));

        var statusRows = await this._opencrmContext.SubscriptionPayments
            .AsNoTracking()
            .Where(payment => payment.CreatedAt >= thirtyDaysAgo && payment.CreatedAt < tomorrow)
            .Select(payment => new
            {
                payment.PaymentStatus,
                payment.Amount,
                NeedsAttention = this._opencrmContext.SubscriptionPurchases.Any(purchase =>
                    purchase.SubscriptionPaymentId == payment.Id
                    && purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention)
            })
            .GroupBy(payment => new { payment.PaymentStatus, payment.NeedsAttention })
            .Select(group => new PaymentStatusRow(
                group.Key.PaymentStatus,
                group.Key.NeedsAttention,
                group.Count(),
                group.Sum(payment => payment.Amount)))
            .ToListAsync();

        var statusBreakdown = statusRows
            .GroupBy(row => row.NeedsAttention
                ? "problem"
                : AdminPaymentDisplay.ResolvePaymentStatusGroup(row.Status))
            .Select(group => new AdminBillingStatusGroupDto
            {
                Group = group.Key,
                Label = AdminPaymentDisplay.ResolvePaymentStatusGroupName(group.Key),
                Count = group.Sum(row => row.Count),
                Amount = group.Sum(row => row.Amount)
            })
            .OrderByDescending(row => row.Count)
            .ToList();

        var rawStatusBreakdown = statusRows
            .GroupBy(row => row.Status)
            .Select(group => new AdminBillingRawStatusDto
            {
                Status = string.IsNullOrWhiteSpace(group.Key) ? "unknown" : group.Key,
                Label = AdminPaymentDisplay.ResolvePaymentStatusName(group.Key),
                Group = AdminPaymentDisplay.ResolvePaymentStatusGroup(group.Key),
                Count = group.Sum(row => row.Count),
                Amount = group.Sum(row => row.Amount)
            })
            .OrderByDescending(row => row.Count)
            .ToList();

        return (dailyPayments, statusBreakdown, rawStatusBreakdown);
    }

    /// <summary> Загружает подписки пользователей с истекающими сроками. </summary>
    private async Task<IReadOnlyList<AdminBillingUserSubscriptionDto>> LoadUserSubscriptionsAsync(DateTime now, DateTime sevenDaysAhead)
    {
        var userSubscriptionsRaw = await this._opencrmContext.UserSubscriptions
            .AsNoTracking()
            .OrderBy(subscription => subscription.EndDate < now)
            .ThenBy(subscription => subscription.EndDate)
            .Select(subscription => new UserSubscriptionRow(
                subscription.Id,
                subscription.UserId,
                subscription.User.Email ?? subscription.User.PhoneNumber ?? subscription.UserId,
                subscription.User.Email,
                subscription.User.PhoneNumber,
                subscription.SubscriptionPlanId,
                subscription.SubscriptionPlan.Name,
                subscription.SubscriptionPlan.PlanTypeCode,
                subscription.SubscriptionPlan.IsDemoPlan,
                subscription.SourceCode,
                subscription.StartDate,
                subscription.EndDate,
                subscription.AutoRenewal))
            .ToListAsync();

        var userSubscriptions = userSubscriptionsRaw
            .Select(subscription => new AdminBillingUserSubscriptionDto
            {
                Id = subscription.Id,
                UserId = subscription.UserId,
                User = subscription.User,
                UserEmail = subscription.UserEmail,
                UserPhone = subscription.UserPhone,
                SubscriptionPlanId = subscription.SubscriptionPlanId,
                PlanName = subscription.PlanName,
                PlanTypeCode = subscription.PlanTypeCode,
                IsDemoPlan = subscription.IsDemoPlan,
                Source = subscription.SourceCode,
                StartDate = subscription.StartDate,
                EndDate = subscription.EndDate,
                AutoRenewal = subscription.AutoRenewal,
                IsActive = subscription.StartDate <= now && subscription.EndDate >= now,
                IsExpiringSoon = subscription.StartDate <= now && subscription.EndDate >= now
                    && subscription.EndDate <= sevenDaysAhead,
                DaysLeft = (int)Math.Ceiling((subscription.EndDate - now).TotalDays)
            })
            .ToList();

        var userSubscriptionHistory = await this._opencrmContext.UserSubscriptionHistory
            .AsNoTracking()
            .Where(history => (history.ActualEndDate ?? history.EndDate) < now)
            .OrderByDescending(history => history.CreatedAt)
            .Select(history => new AdminBillingUserSubscriptionDto
            {
                Id = -history.Id,
                UserId = history.UserId,
                User = history.User.Email ?? history.User.PhoneNumber ?? history.UserId,
                UserEmail = history.User.Email,
                UserPhone = history.User.PhoneNumber,
                SubscriptionPlanId = history.SubscriptionPlanId,
                PlanName = history.SubscriptionPlan.Name,
                PlanTypeCode = history.SubscriptionPlan.PlanTypeCode,
                IsDemoPlan = history.SubscriptionPlan.IsDemoPlan,
                IsHistorical = true,
                Source = history.SourceCode,
                StartDate = history.StartDate,
                EndDate = history.ActualEndDate ?? history.EndDate,
                AutoRenewal = history.AutoRenewal,
                IsActive = false,
                DaysLeft = 0
            })
            .ToListAsync();
        var currentSubscriptionKeys = userSubscriptions
            .Select(subscription => (subscription.UserId, subscription.SubscriptionPlanId, subscription.StartDate))
            .ToHashSet();
        userSubscriptions.AddRange(userSubscriptionHistory
            .Where(subscription => !currentSubscriptionKeys.Contains((
                subscription.UserId, subscription.SubscriptionPlanId, subscription.StartDate)))
            .GroupBy(subscription => new
            {
                subscription.UserId,
                subscription.SubscriptionPlanId,
                subscription.StartDate
            })
            .Select(group => group.OrderBy(subscription => subscription.Id).First()));

        return userSubscriptions;
    }

    /// <summary> Загружает подписки организаций с истекающими сроками. </summary>
    private async Task<IReadOnlyList<AdminBillingOrganizationSubscriptionDto>> LoadOrganizationSubscriptionsAsync(DateTime now, DateTime sevenDaysAhead)
    {
        var organizationSubscriptionsRaw = await this._opencrmContext.OrganizationSubscriptions.AsNoTracking()
            .OrderBy(subscription => subscription.EndDate < now)
            .ThenBy(subscription => subscription.EndDate)
            .Select(AdminBillingQueryService.s_organizationSubscriptionProjection)
            .ToListAsync();

        return organizationSubscriptionsRaw
            .Select(subscription => AdminBillingQueryService.ToOrganizationSubscriptionDto(subscription, now, sevenDaysAhead))
            .ToList();
    }

    /// <summary> Преобразует строку подписки организации в DTO. </summary>
    private static AdminBillingOrganizationSubscriptionDto ToOrganizationSubscriptionDto(
        OrganizationSubscriptionRow subscription,
        DateTime now,
        DateTime sevenDaysAhead) =>
        new()
        {
            Id = subscription.Id,
            OrganizationId = subscription.OrganizationId,
            PrimaryScenarioCode = subscription.PrimaryScenarioCode,
            OrganizationName = subscription.OrganizationName,
            Owner = subscription.Owner,
            OwnerUserId = subscription.OwnerUserId,
            SubscriptionPlanId = subscription.SubscriptionPlanId,
            PlanName = subscription.PlanName,
            PlanTypeCode = subscription.PlanTypeCode,
            IsDemoPlan = subscription.IsDemoPlan,
            Source = subscription.SourceCode,
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            AutoRenewal = subscription.AutoRenewal,
            IsActive = subscription.StartDate <= now && subscription.EndDate >= now,
            IsExpiringSoon = subscription.StartDate <= now && subscription.EndDate >= now
                && subscription.EndDate <= sevenDaysAhead,
            DaysLeft = (int)Math.Ceiling((subscription.EndDate - now).TotalDays)
        };

    /// <summary> Загружает KPI панели биллинга. </summary>
    private async Task<AdminBillingKpisDto> LoadKpisAsync(
        DateTime now,
        DateTime sevenDaysAhead,
        IReadOnlyList<AdminBillingDailyMoneyDto> dailyPayments,
        IReadOnlyList<AdminBillingStatusGroupDto> statusBreakdown)
    {
        var totalPayments = await this._opencrmContext.SubscriptionPayments.AsNoTracking().CountAsync();
        var activeUserSubscriptions = await this._opencrmContext.UserSubscriptions.AsNoTracking()
            .CountAsync(subscription => subscription.StartDate <= now && subscription.EndDate >= now);
        var activeOrganizationSubscriptions = await this._opencrmContext.OrganizationSubscriptions.AsNoTracking()
            .CountAsync(subscription => subscription.StartDate <= now && subscription.EndDate >= now);
        var expiringUserSubscriptions7Days = await this._opencrmContext.UserSubscriptions.AsNoTracking()
            .CountAsync(subscription => subscription.StartDate <= now && subscription.EndDate >= now
                && subscription.EndDate <= sevenDaysAhead);
        var expiringOrganizationSubscriptions7Days = await this._opencrmContext.OrganizationSubscriptions.AsNoTracking()
            .CountAsync(subscription => subscription.StartDate <= now && subscription.EndDate >= now
                && subscription.EndDate <= sevenDaysAhead);
        var payments30DaysAmount = dailyPayments.Sum(point => point.Amount);
        var payments30DaysCount = dailyPayments.Sum(point => point.Count);
        var problemPayments30Days = statusBreakdown
            .Where(row => row.Group is "problem" or "unknown")
            .Sum(row => row.Count);
        var pendingPayments30Days = statusBreakdown
            .Where(row => row.Group == "pending")
            .Sum(row => row.Count);

        return new AdminBillingKpisDto
        {
            TotalPayments = totalPayments,
            Payments30DaysAmount = payments30DaysAmount,
            Payments30DaysCount = payments30DaysCount,
            ProblemPayments30Days = problemPayments30Days,
            PendingPayments30Days = pendingPayments30Days,
            ActiveUserSubscriptions = activeUserSubscriptions,
            ActiveOrganizationSubscriptions = activeOrganizationSubscriptions,
            ExpiringSubscriptions7Days = expiringUserSubscriptions7Days + expiringOrganizationSubscriptions7Days
        };
    }

    /// <summary> Возвращает историю изменений статусов платежа. </summary>
    public async Task<IReadOnlyList<AdminPaymentStatusAuditDto>> GetPaymentStatusAuditsAsync(int paymentId)
    {
        var audits = await (
                from audit in this._opencrmContext.AdminPaymentStatusAudits.AsNoTracking()
                where audit.PaymentId == paymentId
                join user in this._opencrmContext.ApplicationUsers.AsNoTracking()
                    on audit.ChangedByUserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                orderby audit.ChangedAt descending
                select new AdminPaymentStatusAuditDto
                {
                    Id = audit.Id,
                    PreviousStatus = audit.PreviousStatus,
                    NewStatus = audit.NewStatus,
                    Source = audit.Source,
                    Reason = audit.Reason,
                    ChangedByUserId = audit.ChangedByUserId,
                    ChangedBy = user == null
                        ? audit.ChangedByUserId
                        : user.Email ?? user.PhoneNumber ?? audit.ChangedByUserId,
                    ChangedAt = audit.ChangedAt
                })
            .ToListAsync();

        return audits;
    }

    /// <summary> Возвращает историю изменений подписки. </summary>
    public async Task<IReadOnlyList<AdminSubscriptionAuditDto>> GetSubscriptionAuditsAsync(string scope, int subscriptionId)
    {
        var normalizedScope = scope.Trim().ToLowerInvariant();
        if (normalizedScope is not ("users" or "organizations"))
        {
            return [];
        }

        string? clientId;
        if (normalizedScope == "users" && subscriptionId < 0)
        {
            clientId = await this._opencrmContext.UserSubscriptionHistory
                .AsNoTracking()
                .Where(history => history.Id == -subscriptionId)
                .Select(history => history.UserId)
                .FirstOrDefaultAsync();
        }
        else if (normalizedScope == "users")
        {
            clientId = await this._opencrmContext.UserSubscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Id == subscriptionId)
                .Select(subscription => subscription.UserId)
                .FirstOrDefaultAsync();
        }
        else
        {
            var organizationId = await this._opencrmContext.OrganizationSubscriptions
                .AsNoTracking()
                .Where(subscription => subscription.Id == subscriptionId)
                .Select(subscription => (Guid?)subscription.OrganizationId)
                .FirstOrDefaultAsync();
            clientId = organizationId?.ToString();
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return [];
        }

        return await (
                from audit in this._opencrmContext.AdminSubscriptionAudits.AsNoTracking()
                where audit.Scope == normalizedScope && audit.ClientId == clientId
                join user in this._opencrmContext.ApplicationUsers.AsNoTracking()
                    on audit.ChangedByUserId equals user.Id into users
                from user in users.DefaultIfEmpty()
                orderby audit.ChangedAt descending
                select new AdminSubscriptionAuditDto
                {
                    Id = audit.Id,
                    SubscriptionId = audit.SubscriptionId,
                    Action = audit.Action,
                    PreviousEndDate = audit.PreviousEndDate,
                    NewEndDate = audit.NewEndDate,
                    ChangedBy = user == null
                        ? audit.ChangedByUserId
                        : user.Email ?? user.PhoneNumber ?? audit.ChangedByUserId,
                    ChangedAt = audit.ChangedAt,
                    Reason = audit.Reason
                })
            .ToListAsync();
    }

    /// <summary> Строит серию сумм по дням. </summary>
    private static async Task<IReadOnlyList<AdminBillingDailyMoneyDto>> BuildMoneySeriesAsync(
        DateTime from,
        DateTime to,
        IQueryable<DailyMoneyPoint> query)
    {
        var points = await query.ToDictionaryAsync(point => point.Date, point => point);

        return AdminBillingQueryService.EachDay(from, to)
            .Select(date =>
            {
                var hasPoint = points.TryGetValue(date, out var point);

                return new AdminBillingDailyMoneyDto
                {
                    Date = date,
                    Label = date.ToString("dd.MM"),
                    Amount = hasPoint ? point!.Amount : 0m,
                    Count = hasPoint ? point!.Count : 0
                };
            })
            .ToList();
    }

    /// <summary> Перебирает дни в диапазоне дат. </summary>
    private static IEnumerable<DateTime> EachDay(DateTime from, DateTime to)
    {
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    /// <summary> Точка графика сумм за день. </summary>
    private sealed record DailyMoneyPoint(DateTime Date, decimal Amount, int Count);

    /// <summary> Строка группировки платежей по статусу. </summary>
    private sealed record PaymentStatusRow(string? Status, bool NeedsAttention, int Count, decimal Amount);

    /// <summary> Состояние применения покупки к подписке. </summary>
    private sealed record PaymentPurchaseSummary(Guid Id, string ApplicationStatus, string? LastErrorCode);

    /// <summary> Строка данных платежа. </summary>
    private sealed record PaymentRow(
        int Id,
        string UserId,
        string User,
        string? UserEmail,
        string? UserPhone,
        int SubscriptionPlanId,
        string PaymentProviderCode,
        string PlanName,
        string PlanTypeCode,
        int PlanDurationDays,
        decimal Amount,
        decimal RefundedAmount,
        DateTime? PaidAt,
        decimal? OriginalAmount,
        decimal? PromoDiscountAmount,
        string? PromoCodeSnapshot,
        DateTime CreatedAt,
        DateTime? StatusUpdatedAt,
        string PaymentMethod,
        string PaymentStatus,
        string? TransactionId,
        string? ExternalPaymentId,
        string? PaymentLink);

    /// <summary> Строка данных подписки пользователя. </summary>
    private sealed record UserSubscriptionRow(
        int Id,
        string UserId,
        string User,
        string? UserEmail,
        string? UserPhone,
        int SubscriptionPlanId,
        string PlanName,
        string PlanTypeCode,
        bool IsDemoPlan,
        string SourceCode,
        DateTime StartDate,
        DateTime EndDate,
        bool AutoRenewal);

    /// <summary> Строка данных подписки организации. </summary>
    private sealed record OrganizationSubscriptionRow(
        int Id,
        Guid OrganizationId,
        string PrimaryScenarioCode,
        string OrganizationName,
        string Owner,
        string OwnerUserId,
        int SubscriptionPlanId,
        string PlanName,
        string PlanTypeCode,
        bool IsDemoPlan,
        string SourceCode,
        DateTime StartDate,
        DateTime EndDate,
        bool AutoRenewal);
}
