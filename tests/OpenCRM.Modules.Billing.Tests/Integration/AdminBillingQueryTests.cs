using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class AdminBillingQueryTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task OrganizationSubscriptionIncludesItsScenarioCode()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Detailing);
        var plan = await fixture.CreatePlanAsync(context, "Подписка детейлинга");
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30)
        });
        await context.SaveChangesAsync();

        var result = await CreateQueryService(context).GetAsync();

        Assert.Equal(OrganizationTypeCodes.Detailing,
            Assert.Single(result.Subscriptions.Organizations).PrimaryScenarioCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(210)]
    [InlineData(420)]
    public async Task PaymentDetailsKeepVerifiedMoneyAndGrantedPeriodAfterManualAccessStop(decimal refundedAmount)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "payment-summary@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Сводка платежа");
        plan.Price = 420;
        var organization = await fixture.CreateOrganizationAsync(context, user.Id);
        var timestamp = DateTime.UtcNow.AddDays(-2);
        var grantedFrom = new DateTime(timestamp.Ticks - timestamp.Ticks % 10, DateTimeKind.Utc);
        var grantedUntil = grantedFrom.AddDays(30);
        var purchase = await fixture.AddPurchaseSnapshotAsync(context, user.Id, organization.Id, plan, "{}", grantedFrom, grantedUntil);
        purchase.AppliedAt = grantedFrom;
        purchase.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
        purchase.LastErrorCode = "operation_event_mismatch";
        purchase.RefundedAmountMinor = decimal.ToInt64(refundedAmount * 100);
        var payment = await context.SubscriptionPayments.SingleAsync(item => item.Id == purchase.SubscriptionPaymentId);
        payment.PaidAt = grantedFrom;
        payment.RefundedAmount = refundedAmount;
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = grantedFrom,
            EndDate = grantedFrom.AddDays(1),
            SourceCode = "admin"
        });
        await context.SaveChangesAsync();

        var details = await CreateQueryService(context).GetPaymentAsync(payment.Id);

        Assert.NotNull(details);
        Assert.Equal(420, details.Amount);
        Assert.Equal(refundedAmount, details.RefundedAmount);
        Assert.Equal(grantedFrom, details.PaidAt);
        Assert.Equal("Succeeded", details.ProviderStatus);
        Assert.Equal(grantedFrom, details.AppliedAt);
        Assert.Equal(grantedFrom, details.GrantedFrom);
        Assert.Equal(grantedUntil, details.GrantedUntil);
        Assert.True(details.IsTest);
        Assert.Equal(organization.Id, details.OrganizationId);
        Assert.Equal(organization.Name, details.OrganizationName);
        Assert.Equal("operation_event_mismatch", details.LastErrorCode);
        Assert.True((await context.OrganizationSubscriptions.SingleAsync()).EndDate < DateTime.UtcNow);
    }

    [Fact]
    public async Task PaymentDetailsExposeOnlyBoundedSafePaymentServiceDiagnostics()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "diagnostics@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Платёжный сервис");
        var purchase = await fixture.AddPurchaseSnapshotAsync(context, user.Id, null, plan, "{}",
            DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
        purchase.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
        purchase.LastErrorCode = "activation_failed";
        var now = DateTime.UtcNow;
        var events = Enumerable.Range(0, 22).Select(index => new SubscriptionPaymentEvent
        {
            PurchaseId = purchase.Id,
            MessageId = Guid.NewGuid(),
            ProviderOrderId = Guid.NewGuid(),
            ProviderVersion = index,
            Kind = "state",
            Code = index == 21 ? "activation_failed" : "state_received",
            Payload = JsonSerializer.Serialize(new { PrivateValue = "sensitive payload for " + user.Email }),
            AmountMinor = 12500,
            RefundedAmountMinor = 2500,
            CreatedAt = now.AddMinutes(index),
            ProcessedAt = now.AddMinutes(index),
            NextAttemptAt = now.AddMinutes(index)
        }).ToArray();
        context.SubscriptionPaymentEvents.AddRange(events);
        await context.SaveChangesAsync();

        var payment = await new AdminBillingQueryService(context).GetPaymentAsync(purchase.SubscriptionPaymentId);
        Assert.NotNull(payment);
        var attentionPage = await new AdminBillingQueryService(context).GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            Status = "problem",
            PeriodDays = 0
        });

        Assert.Contains(payment.Id, attentionPage.Items.Select(item => item.Id));
        Assert.Equal("payments-service", payment.PaymentProviderCode);
        Assert.Equal(purchase.Id, payment.PurchaseId);
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, payment.ApplicationStatus);
        Assert.Equal("activation_failed", payment.LastErrorCode);
        Assert.True(payment.IsProblem);
        Assert.Equal("problem", payment.StatusGroup);
        Assert.Equal(DateTimeKind.Utc, payment.ProviderEvents[0].CreatedAt.Kind);
        Assert.Equal(20, payment.ProviderEvents.Count);
        Assert.Equal(events[^1].Id, payment.ProviderEvents[0].Id);
        Assert.Equal(events[^1].MessageId, payment.ProviderEvents[0].MessageId);
        Assert.Equal(events[^1].ProviderOrderId, payment.ProviderEvents[0].ProviderOrderId);
        Assert.Equal(12500, payment.ProviderEvents[0].AmountMinor);
        Assert.Equal(2500, payment.ProviderEvents[0].RefundedAmountMinor);
        Assert.Equal("RUB", payment.ProviderEvents[0].Currency);
        Assert.DoesNotContain("sensitive payload", JsonSerializer.Serialize(payment));
    }

    [Fact]
    public async Task AttentionAndPurchasesApplyPaidStatusRefundAndReceiptRules()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "buyer@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Покупка");
        var overdue = DateTime.UtcNow.AddDays(-2);
        var payments = new[]
        {
            CreatePayment(user, plan, "completed", overdue, amount: 100),
            CreatePayment(user, plan, "failed", overdue, amount: 200),
            CreatePayment(user, plan, "failed", overdue, amount: 300, refundedAmount: 300),
            CreatePayment(user, plan, "completed", overdue, amount: 400, receiptIssuedAt: DateTime.UtcNow),
            CreatePayment(user, plan, "refunded", overdue, amount: 500),
            CreatePayment(user, plan, "pending", overdue, amount: 600),
            CreatePayment(user, plan, "completed", null, amount: 700)
        };
        context.SubscriptionPayments.AddRange(payments);
        await context.SaveChangesAsync();
        var query = CreateQueryService(context);

        var attention = await query.GetAttentionAsync();
        var purchases = await query.GetPurchasesAsync();

        Assert.Null(attention.PeriodDays);
        Assert.Equal(2, attention.MissingReceiptCount);
        Assert.Equal(2, attention.OverdueReceiptCount);
        Assert.Equal(1, attention.FailedActivationCount);
        Assert.Equal(5, purchases.Count);
        Assert.DoesNotContain(purchases, purchase => purchase.PaymentId == payments[5].Id);
        Assert.DoesNotContain(purchases, purchase => purchase.PaymentId == payments[6].Id);
        Assert.Contains(purchases, purchase => purchase.PaymentId == payments[4].Id);
        Assert.Equal("not_sent", Assert.Single(purchases, purchase => purchase.PaymentId == payments[0].Id).NpdReceiptDeliveryStatus);
    }

    [Fact]
    public async Task AttentionCountsReceiptsWithinUtcPeriodAndKeepsFailedActivationsGlobal()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "attention-period@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Период чеков");
        var utcToday = DateTime.UtcNow.Date;
        var utcMidday = utcToday.AddHours(12);
        var periodStart = utcToday.AddDays(-6);
        var payments = new[]
        {
            CreatePayment(user, plan, "completed", utcToday, amount: 100),
            CreatePayment(user, plan, "completed", utcMidday.AddHours(-1), amount: 200, refundedAmount: 50),
            CreatePayment(user, plan, "failed", utcMidday.AddHours(-2), amount: 300),
            CreatePayment(user, plan, "completed", utcToday.AddDays(-1), amount: 400),
            CreatePayment(user, plan, "completed", periodStart, amount: 500),
            CreatePayment(user, plan, "failed", periodStart.AddMilliseconds(-1), amount: 600),
            CreatePayment(user, plan, "completed", utcToday.AddDays(-29), amount: 650),
            CreatePayment(user, plan, "failed", utcToday.AddDays(-31), amount: 675),
            CreatePayment(user, plan, "completed", utcToday.AddDays(1), amount: 700),
            CreatePayment(user, plan, "completed", utcToday, amount: 800, receiptIssuedAt: utcToday),
            CreatePayment(user, plan, "completed", utcToday, amount: 900, refundedAmount: 900),
            CreatePayment(user, plan, "completed", utcMidday, amount: 1000, refundedAmount: 100),
            CreatePayment(user, plan, "pending", utcToday, amount: 1100),
            CreatePayment(user, plan, "failed", null, amount: 1200)
        };
        context.SubscriptionPayments.AddRange(payments);
        await context.SaveChangesAsync();
        var query = CreateQueryService(context);

        var sevenDays = await query.GetAttentionAsync(7);
        var oneDay = await query.GetAttentionAsync(1);
        var thirtyDays = await query.GetAttentionAsync(30);

        Assert.Equal(7, sevenDays.PeriodDays);
        Assert.Equal(6, sevenDays.MissingReceiptCount);
        Assert.Equal(2, sevenDays.OverdueReceiptCount);
        Assert.Equal(3, sevenDays.FailedActivationCount);
        Assert.Equal(1, oneDay.PeriodDays);
        Assert.Equal(4, oneDay.MissingReceiptCount);
        Assert.Equal(0, oneDay.OverdueReceiptCount);
        Assert.Equal(3, oneDay.FailedActivationCount);
        Assert.Equal(30, thirtyDays.PeriodDays);
        Assert.Equal(8, thirtyDays.MissingReceiptCount);
        Assert.Equal(4, thirtyDays.OverdueReceiptCount);
        Assert.Equal(3, thirtyDays.FailedActivationCount);
    }

    [Fact]
    public async Task AttentionNormalizesUnsupportedPeriodToOneUtcDay()
    {
        await using var context = fixture.CreateContext();
        var query = CreateQueryService(context);

        var attention = await query.GetAttentionAsync(14);

        Assert.Equal(1, attention.PeriodDays);
        Assert.Equal(0, attention.MissingReceiptCount);
        Assert.Equal(0, attention.OverdueReceiptCount);
    }

    [Fact]
    public async Task PurchasesResolveReceiptDeliveryStatesAndKeepMissingMessagesUnsent()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "receipt@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Чек");
        var sentAtTimestamp = DateTime.UtcNow.AddMinutes(-4);
        var sentAt = new DateTime(sentAtTimestamp.Ticks - sentAtTimestamp.Ticks % 10, DateTimeKind.Utc);
        var statuses = new[]
        {
            EmailMessageStatusCodes.Failed,
            EmailMessageStatusCodes.HardBounced,
            EmailMessageStatusCodes.SoftBounced,
            EmailMessageStatusCodes.Spam,
            EmailMessageStatusCodes.SpamBlock,
            EmailMessageStatusCodes.Unsubscribed,
            EmailMessageStatusCodes.Delivered,
            EmailMessageStatusCodes.Opened,
            EmailMessageStatusCodes.Clicked,
            EmailMessageStatusCodes.Simulated,
            EmailMessageStatusCodes.Accepted,
            EmailMessageStatusCodes.Pending
        };
        var messages = statuses.Select((status, index) => new EmailMessage
        {
            ToEmail = user.Email!,
            Subject = "Чек",
            StatusCode = status,
            SentAt = index == 10 ? sentAt : null,
            ErrorMessage = status == EmailMessageStatusCodes.Failed ? "Временная ошибка" : null
        }).ToArray();
        context.EmailMessages.AddRange(messages);
        await context.SaveChangesAsync();
        var payments = messages.Select((message, index) =>
        {
            var payment = CreatePayment(user, plan, "completed", DateTime.UtcNow.AddMinutes(-index), amount: 100 + index);
            payment.NpdReceiptEmailMessageId = message.Id;
            return payment;
        }).ToArray();
        var noMessage = CreatePayment(user, plan, "completed", DateTime.UtcNow, amount: 200);
        noMessage.NpdReceiptEmailMessageId = int.MaxValue;
        context.SubscriptionPayments.AddRange(payments.Append(noMessage));
        await context.SaveChangesAsync();
        var query = CreateQueryService(context);

        var purchases = await query.GetPurchasesAsync();

        Assert.Equal("failed", FindPurchase(purchases, payments[0]).NpdReceiptDeliveryStatus);
        Assert.Equal("failed", FindPurchase(purchases, payments[5]).NpdReceiptDeliveryStatus);
        Assert.Equal("delivered", FindPurchase(purchases, payments[6]).NpdReceiptDeliveryStatus);
        Assert.Equal("delivered", FindPurchase(purchases, payments[8]).NpdReceiptDeliveryStatus);
        Assert.Equal("simulated", FindPurchase(purchases, payments[9]).NpdReceiptDeliveryStatus);
        Assert.Equal("sent", FindPurchase(purchases, payments[10]).NpdReceiptDeliveryStatus);
        Assert.Equal("queued", FindPurchase(purchases, payments[11]).NpdReceiptDeliveryStatus);
        Assert.Equal("not_sent", FindPurchase(purchases, noMessage).NpdReceiptDeliveryStatus);
        Assert.Equal(user.Email, FindPurchase(purchases, payments[0]).NpdReceiptSentTo);
        Assert.Equal("Временная ошибка", FindPurchase(purchases, payments[0]).NpdReceiptSendError);
        Assert.Equal(sentAt, FindPurchase(purchases, payments[10]).NpdReceiptSentAt);
    }

    [Fact]
    public async Task AvailablePlansReturnOnlyActivePurchasablePlansInCatalogOrder()
    {
        await using var context = fixture.CreateContext();
        var later = await fixture.CreatePlanAsync(context, "Позже");
        later.Order = 2;
        var firstByName = await fixture.CreatePlanAsync(context, "Альфа");
        firstByName.Order = 1;
        var firstByOrder = await fixture.CreatePlanAsync(context, "Бета");
        firstByOrder.Order = 1;
        var inactive = await fixture.CreatePlanAsync(context, "Неактивный");
        inactive.IsActive = false;
        await fixture.CreatePlanAsync(context, "Демо", isDemo: true);
        await fixture.CreatePlanAsync(context, "По умолчанию", isDefault: true);
        await context.SaveChangesAsync();
        var query = CreateQueryService(context);

        var plans = await query.GetAvailablePlansAsync();

        Assert.Equal([firstByName.Id, firstByOrder.Id, later.Id], plans.Select(plan => plan.Id));
        Assert.All(plans, plan => Assert.Equal(30, plan.DurationDays));
    }

    [Fact]
    public async Task PaymentAndDashboardUseEmailPhoneThenIdAndReturnThirtyDailyPoints()
    {
        await using var context = fixture.CreateContext();
        var emailUser = new ApplicationUser { Email = "email@example.test", PhoneNumber = "+70000000001" };
        var phoneUser = new ApplicationUser { PhoneNumber = "+70000000002" };
        var idUser = new ApplicationUser();
        context.Users.AddRange(emailUser, phoneUser, idUser);
        var plan = await fixture.CreatePlanAsync(context, "Личный");
        var now = DateTime.UtcNow;
        var emailPayment = CreatePayment(emailUser, plan, "completed", now, amount: 100);
        var phonePayment = CreatePayment(phoneUser, plan, "refunded", now.AddDays(-1), amount: 200, refundedAmount: 50);
        var idPayment = CreatePayment(idUser, plan, "failed", now.AddDays(-2), amount: 300);
        var outsideWindow = CreatePayment(emailUser, plan, "completed", now.AddDays(-31), amount: 400);
        var unpaid = CreatePayment(emailUser, plan, "   ", null, amount: 500);
        unpaid.CreatedAt = now;
        context.SubscriptionPayments.AddRange(emailPayment, phonePayment, idPayment, outsideWindow, unpaid);
        await context.SaveChangesAsync();
        var query = CreateQueryService(context);

        var email = await query.GetPaymentAsync(emailPayment.Id);
        var phone = await query.GetPaymentAsync(phonePayment.Id);
        var byId = await query.GetPaymentAsync(idPayment.Id);
        var dashboard = await query.GetAsync();

        Assert.Equal(emailUser.Email, email!.User);
        Assert.Equal(phoneUser.PhoneNumber, phone!.User);
        Assert.Equal(idUser.Id, byId!.User);
        Assert.Null(await query.GetPaymentAsync(int.MaxValue));
        Assert.Equal(30, dashboard.Charts.DailyPayments.Count);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(-29), dashboard.Charts.DailyPayments[0].Date);
        Assert.Equal(DateTime.UtcNow.Date, dashboard.Charts.DailyPayments[^1].Date);
        Assert.Equal(3, dashboard.Kpis.Payments30DaysCount);
        Assert.Equal(550, dashboard.Kpis.Payments30DaysAmount);
        Assert.Equal(5, dashboard.Kpis.TotalPayments);
        Assert.Equal(5, dashboard.Payments.Count);
        Assert.Contains(dashboard.Payments, payment => payment.Id == unpaid.Id);
        var unknownStatus = Assert.Single(dashboard.RawStatusBreakdown, status => status.Status == "unknown");
        Assert.Equal("Без статуса", unknownStatus.Label);
        Assert.Equal("unknown", unknownStatus.Group);
    }

    [Fact]
    public async Task DashboardMovesNeedsAttentionPaymentsIntoProblemGroupWithinThirtyDays()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "attention-kpi@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Проблемная покупка");
        var now = DateTime.UtcNow;

        await AddPaymentAsync("completed", 100, needsAttention: true, now);
        await AddPaymentAsync("pending", 200, needsAttention: true, now);
        await AddPaymentAsync("failed", 300, needsAttention: true, now);
        await AddPaymentAsync("completed", 110, needsAttention: false, now);
        await AddPaymentAsync("pending", 220, needsAttention: false, now);
        await AddPaymentAsync("failed", 330, needsAttention: false, now);
        await AddPaymentAsync("pending", 80, needsAttention: true, now.Date.AddDays(-29));
        await AddPaymentAsync("completed", 900, needsAttention: true, now.AddDays(1));
        await AddPaymentAsync("completed", 500, needsAttention: true, now.AddDays(-31));
        await context.SaveChangesAsync();

        var dashboard = await CreateQueryService(context).GetAsync();
        var problemGroup = Assert.Single(dashboard.Charts.StatusBreakdown, row => row.Group == "problem");
        var pendingGroup = Assert.Single(dashboard.Charts.StatusBreakdown, row => row.Group == "pending");
        var successGroup = Assert.Single(dashboard.Charts.StatusBreakdown, row => row.Group == "success");

        Assert.Equal(5, problemGroup.Count);
        Assert.Equal(1010, problemGroup.Amount);
        Assert.Equal(1, pendingGroup.Count);
        Assert.Equal(220, pendingGroup.Amount);
        Assert.Equal(1, successGroup.Count);
        Assert.Equal(110, successGroup.Amount);
        Assert.Equal(5, dashboard.Kpis.ProblemPayments30Days);
        Assert.Equal(1, dashboard.Kpis.PendingPayments30Days);

        var rawCompleted = Assert.Single(dashboard.RawStatusBreakdown, row => row.Status == "completed");
        var rawPending = Assert.Single(dashboard.RawStatusBreakdown, row => row.Status == "pending");
        var rawFailed = Assert.Single(dashboard.RawStatusBreakdown, row => row.Status == "failed");
        Assert.Equal(2, rawCompleted.Count);
        Assert.Equal(210, rawCompleted.Amount);
        Assert.Equal(3, rawPending.Count);
        Assert.Equal(500, rawPending.Amount);
        Assert.Equal(2, rawFailed.Count);
        Assert.Equal(630, rawFailed.Amount);

        foreach (var group in new[] { "success", "pending", "problem" })
        {
            var page = await CreateQueryService(context).GetPaymentsPageAsync(new AdminBillingPageQuery
            {
                Status = group,
                PeriodDays = 0
            });

            Assert.NotEmpty(page.Items);
            Assert.All(page.Items, payment => Assert.Equal(group, payment.StatusGroup));
        }

        async Task AddPaymentAsync(string status, decimal amount, bool needsAttention, DateTime createdAt)
        {
            var purchase = await fixture.AddPurchaseSnapshotAsync(context, user.Id, null, plan, "{}", now, now.AddDays(30));
            purchase.ApplicationStatus = needsAttention
                ? SubscriptionPurchaseStatuses.NeedsAttention
                : SubscriptionPurchaseStatuses.Applied;
            var payment = await context.SubscriptionPayments.SingleAsync(item => item.Id == purchase.SubscriptionPaymentId);
            payment.PaymentStatus = status;
            payment.Amount = amount;
            payment.CreatedAt = createdAt;
        }
    }

    [Fact]
    public async Task DashboardIncludesSubscriptionStatesAndDeDuplicatesUserHistory()
    {
        await using var context = fixture.CreateContext();
        var activeUser = new ApplicationUser { Email = "active@example.test" };
        var expiredUser = new ApplicationUser { Email = "expired@example.test" };
        var futureUser = new ApplicationUser { Email = "future@example.test" };
        var historyUser = new ApplicationUser { Email = "history@example.test" };
        var demoUser = new ApplicationUser { Email = "demo@example.test" };
        context.Users.AddRange(activeUser, expiredUser, futureUser, historyUser, demoUser);
        var currentPlan = await fixture.CreatePlanAsync(context, "Активный");
        var expiredPlan = await fixture.CreatePlanAsync(context, "Истекший");
        var futurePlan = await fixture.CreatePlanAsync(context, "Будущий");
        var historyPlan = await fixture.CreatePlanAsync(context, "История");
        var demoPlan = await fixture.CreatePlanAsync(context, "Демо подписка", isDemo: true);
        var now = DateTime.UtcNow;
        context.UserSubscriptions.AddRange(
            new UserSubscription { User = activeUser, SubscriptionPlan = currentPlan, StartDate = now.AddDays(-1), EndDate = now.AddDays(3) },
            new UserSubscription { User = expiredUser, SubscriptionPlan = expiredPlan, StartDate = now.AddDays(-10), EndDate = now.AddDays(-1) },
            new UserSubscription { User = futureUser, SubscriptionPlan = futurePlan, StartDate = now.AddDays(1), EndDate = now.AddDays(31) },
            new UserSubscription { User = historyUser, SubscriptionPlan = historyPlan, StartDate = now.AddDays(-10), EndDate = now.AddDays(10) },
            new UserSubscription
            {
                User = demoUser,
                SubscriptionPlan = demoPlan,
                StartDate = now.AddDays(-1),
                EndDate = now.AddDays(10),
                SourceCode = "demo"
            });
        context.UserSubscriptionHistory.AddRange(
            new UserSubscriptionHistory { User = historyUser, SubscriptionPlan = historyPlan,
                StartDate = now.AddDays(-10), EndDate = now.AddDays(-1), ActualEndDate = now.AddDays(-1), CreatedAt = now },
            new UserSubscriptionHistory { User = historyUser, SubscriptionPlan = historyPlan,
                StartDate = now.AddDays(-10), EndDate = now.AddDays(-2), ActualEndDate = now.AddDays(-2), CreatedAt = now.AddDays(-1) },
            new UserSubscriptionHistory { User = historyUser, SubscriptionPlan = historyPlan,
                StartDate = now.AddDays(-20), EndDate = now.AddDays(-10), ActualEndDate = now.AddDays(-10), CreatedAt = now.AddDays(-2) });
        var activeOrganization = await fixture.CreateOrganizationAsync(context);
        var expiredOrganization = await fixture.CreateOrganizationAsync(context);
        var futureOrganization = await fixture.CreateOrganizationAsync(context);
        var demoOrganization = await fixture.CreateOrganizationAsync(context);
        context.OrganizationSubscriptions.AddRange(
            new OrganizationSubscription { OrganizationId = activeOrganization.Id, SubscriptionPlan = currentPlan,
                StartDate = now.AddDays(-1), EndDate = now.AddDays(3) },
            new OrganizationSubscription { OrganizationId = expiredOrganization.Id, SubscriptionPlan = expiredPlan,
                StartDate = now.AddDays(-10), EndDate = now.AddDays(-1) },
            new OrganizationSubscription { OrganizationId = futureOrganization.Id, SubscriptionPlan = futurePlan,
                StartDate = now.AddDays(1), EndDate = now.AddDays(31) },
            new OrganizationSubscription { OrganizationId = demoOrganization.Id, SubscriptionPlan = demoPlan,
                StartDate = now.AddDays(-1), EndDate = now.AddDays(10), SourceCode = "demo" });
        await context.SaveChangesAsync();
        var query = CreateQueryService(context);

        var dashboard = await query.GetAsync();

        Assert.Equal(3, dashboard.Kpis.ActiveUserSubscriptions);
        Assert.Equal(2, dashboard.Kpis.ActiveOrganizationSubscriptions);
        Assert.True(dashboard.Kpis.ExpiringSubscriptions7Days == 2,
            string.Join("; ", dashboard.Subscriptions.Users.Where(subscription => subscription.IsExpiringSoon)
                .Select(subscription => $"user:{subscription.UserEmail}:{subscription.EndDate:O}"))
            + "; " + string.Join("; ", dashboard.Subscriptions.Organizations.Where(subscription => subscription.IsExpiringSoon)
                .Select(subscription => $"org:{subscription.OrganizationName}:{subscription.EndDate:O}")));
        Assert.Equal(6, dashboard.Subscriptions.Users.Count);
        var historyEntries = dashboard.Subscriptions.Users
            .Where(subscription => subscription.UserId == historyUser.Id && subscription.SubscriptionPlanId == historyPlan.Id)
            .ToArray();
        Assert.Equal(2, historyEntries.Length);
        Assert.Single(historyEntries, subscription => subscription.IsHistorical);
        Assert.Contains(dashboard.Subscriptions.Users, subscription => subscription.UserId == expiredUser.Id
            && !subscription.IsActive && !subscription.IsHistorical);
        Assert.Contains(dashboard.Subscriptions.Users, subscription => subscription.UserId == futureUser.Id
            && !subscription.IsActive && !subscription.IsHistorical);
        Assert.Contains(dashboard.Subscriptions.Users, subscription => subscription.UserId == demoUser.Id
            && subscription.IsDemoPlan);
        Assert.Equal(4, dashboard.Subscriptions.Organizations.Count);
        Assert.Contains(dashboard.Subscriptions.Organizations, subscription => subscription.OrganizationId == expiredOrganization.Id
            && !subscription.IsActive);
        Assert.Contains(dashboard.Subscriptions.Organizations, subscription => subscription.OrganizationId == futureOrganization.Id
            && !subscription.IsActive);
        Assert.Contains(dashboard.Subscriptions.Organizations, subscription => subscription.OrganizationId == demoOrganization.Id
            && subscription.IsDemoPlan);
    }

    [Fact]
    public async Task DashboardAndPaymentQueriesCapRowsAndKeepOnlyTopEightPlanRevenueGroups()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "many@example.test" };
        context.Users.Add(user);
        var plans = new List<SubscriptionPlan>();
        for (var index = 0; index < 9; index++)
        {
            plans.Add(await fixture.CreatePlanAsync(context, $"План {index}"));
        }

        var now = DateTime.UtcNow;
        var payments = new List<SubscriptionPayment>();
        for (var index = 0; index < 305; index++)
        {
            var amount = index % plans.Count == 8 ? 1 : 100;
            payments.Add(CreatePayment(user, plans[index % plans.Count], "completed", now.AddMinutes(-index), amount));
        }

        context.SubscriptionPayments.AddRange(payments);
        await context.SaveChangesAsync();
        var query = CreateQueryService(context);

        var dashboard = await query.GetAsync();

        Assert.Equal(300, dashboard.Payments.Count);
        Assert.Equal(8, dashboard.Charts.PlanRevenue.Count);
        Assert.DoesNotContain(dashboard.Charts.PlanRevenue, revenue => revenue.PlanId == plans[8].Id);
        Assert.Equal(payments.OrderByDescending(payment => payment.CreatedAt).Take(300).Select(payment => payment.Id),
            dashboard.Payments.Select(payment => payment.Id));
    }

    [Fact]
    public async Task AuditQueriesScopeCurrentHistoricalAndOrganizationSubscriptionsAndUseFallbackActorNames()
    {
        await using var context = fixture.CreateContext();
        var buyer = new ApplicationUser { Email = "audit@example.test" };
        var phoneActor = new ApplicationUser { PhoneNumber = "+70000000003" };
        var emailActor = new ApplicationUser { Email = "admin@example.test" };
        context.Users.AddRange(buyer, phoneActor, emailActor);
        var plan = await fixture.CreatePlanAsync(context, "Аудит");
        var payment = CreatePayment(buyer, plan, "completed", DateTime.UtcNow, amount: 100);
        context.SubscriptionPayments.Add(payment);
        var userSubscription = new UserSubscription { User = buyer, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(3) };
        var history = new UserSubscriptionHistory { User = buyer, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-10), EndDate = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow };
        context.UserSubscriptions.Add(userSubscription);
        context.UserSubscriptionHistory.Add(history);
        var organization = await fixture.CreateOrganizationAsync(context, buyer.Id);
        var orgSubscription = new OrganizationSubscription { OrganizationId = organization.Id, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(3) };
        context.OrganizationSubscriptions.Add(orgSubscription);
        await context.SaveChangesAsync();
        var timestamp = DateTime.UtcNow;
        context.AdminPaymentStatusAudits.AddRange(
            new AdminPaymentStatusAudit { PaymentId = payment.Id, PreviousStatus = "pending", NewStatus = "completed",
                Source = "admin", ChangedByUserId = phoneActor.Id, ChangedAt = timestamp.AddMinutes(-1) },
            new AdminPaymentStatusAudit { PaymentId = payment.Id, PreviousStatus = "completed", NewStatus = "refunded",
                Source = "admin", ChangedByUserId = "deleted-admin", ChangedAt = timestamp });
        context.AdminSubscriptionAudits.AddRange(
            new AdminSubscriptionAudit { Scope = "users", SubscriptionId = userSubscription.Id, ClientId = buyer.Id,
                Action = "extended", ChangedByUserId = emailActor.Id, ChangedAt = timestamp, Reason = "Текущая" },
            new AdminSubscriptionAudit { Scope = "users", SubscriptionId = -history.Id, ClientId = buyer.Id,
                Action = "expired", ChangedByUserId = "deleted-admin", ChangedAt = timestamp.AddMinutes(-1), Reason = "История" },
            new AdminSubscriptionAudit { Scope = "organizations", SubscriptionId = orgSubscription.Id,
                ClientId = organization.Id.ToString(), Action = "extended", ChangedByUserId = phoneActor.Id,
                ChangedAt = timestamp, Reason = "Организация" },
            new AdminSubscriptionAudit { Scope = "users", SubscriptionId = 12345, ClientId = "unrelated-user",
                Action = "extended", ChangedByUserId = "deleted-admin", ChangedAt = timestamp, Reason = "Чужая" },
            new AdminSubscriptionAudit { Scope = "organizations", SubscriptionId = 12345,
                ClientId = organization.Id.ToString(), Action = "extended", ChangedByUserId = "deleted-admin",
                ChangedAt = timestamp.AddMinutes(-2), Reason = "Другой id клиента" });
        await context.SaveChangesAsync();
        var query = CreateQueryService(context);

        var paymentAudits = await query.GetPaymentStatusAuditsAsync(payment.Id);
        var currentUserAudits = await query.GetSubscriptionAuditsAsync(" USERS ", userSubscription.Id);
        var historicalUserAudits = await query.GetSubscriptionAuditsAsync("users", -history.Id);
        var organizationAudits = await query.GetSubscriptionAuditsAsync("organizations", orgSubscription.Id);

        Assert.Equal(2, paymentAudits.Count);
        Assert.Equal("deleted-admin", paymentAudits[0].ChangedBy);
        Assert.Equal(phoneActor.PhoneNumber, paymentAudits[1].ChangedBy);
        Assert.Equal(2, currentUserAudits.Count);
        Assert.Equal(emailActor.Email, currentUserAudits[0].ChangedBy);
        Assert.Equal(2, historicalUserAudits.Count);
        Assert.Equal(emailActor.Email, historicalUserAudits[0].ChangedBy);
        Assert.Equal(2, organizationAudits.Count);
        Assert.Contains(organizationAudits, audit => audit.ChangedBy == phoneActor.PhoneNumber);
        Assert.Contains(organizationAudits, audit => audit.ChangedBy == "deleted-admin");
        Assert.Empty(await query.GetPaymentStatusAuditsAsync(int.MaxValue));
        Assert.Empty(await query.GetSubscriptionAuditsAsync("unknown", userSubscription.Id));
        Assert.Empty(await query.GetSubscriptionAuditsAsync("users", int.MaxValue));
        Assert.Empty(await query.GetSubscriptionAuditsAsync("users", int.MinValue));
        Assert.Empty(await query.GetSubscriptionAuditsAsync("organizations", int.MaxValue));
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static IAdminBillingQueryService CreateQueryService(OpenCRMContext context)
    {
        return new AdminBillingQueryService(context);
    }

    private static SubscriptionPayment CreatePayment(
        ApplicationUser user,
        SubscriptionPlan plan,
        string status,
        DateTime? paidAt,
        decimal amount,
        decimal refundedAmount = 0,
        DateTime? receiptIssuedAt = null)
    {
        var createdAt = paidAt ?? DateTime.UtcNow;
        return new SubscriptionPayment
        {
            User = user,
            SubscriptionPlan = plan,
            Amount = amount,
            RefundedAmount = refundedAmount,
            CreatedAt = createdAt,
            StatusUpdatedAt = createdAt,
            PaidAt = paidAt,
            PaymentMethod = "yookassa",
            PaymentStatus = status,
            NpdReceiptIssuedAt = receiptIssuedAt
        };
    }

    private static AdminBillingPurchaseDto FindPurchase(IReadOnlyList<AdminBillingPurchaseDto> purchases, SubscriptionPayment payment)
    {
        return Assert.Single(purchases, purchase => purchase.PaymentId == payment.Id);
    }

}
