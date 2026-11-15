using System.Globalization;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class AdminBillingPaginationTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task PaymentsFilterBeforePagingAndClampPageBounds()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser
        {
            Id = "pagination-user",
            Email = "pagination@example.test",
            PhoneNumber = "+79990000000"
        };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "План постраничного списка");
        var today = DateTime.UtcNow.Date;
        var payments = new[]
        {
            CreatePayment(user, plan, "completed", today.AddHours(1), 500, "success-a"),
            CreatePayment(user, plan, "succeeded", today.AddHours(1), 500, "success-b"),
            CreatePayment(user, plan, "paid", today.AddHours(1), 500, "success-c"),
            CreatePayment(user, plan, "pending", today.AddHours(2), 100),
            CreatePayment(user, plan, "canceled_by_gateway", today.AddHours(3), 100),
            CreatePayment(user, plan, "partial_refund", today.AddHours(4), 100),
            CreatePayment(user, plan, "gateway_error", today.AddHours(5), 100),
            CreatePayment(user, plan, "failed", today.AddHours(6), 100, paidAt: today.AddHours(6)),
            CreatePayment(user, plan, "failed", today.AddHours(7), 100),
            CreatePayment(user, plan, "unmapped_state", today.AddHours(8), 100),
            CreatePayment(user, plan, "completed", today.AddDays(-8), 100),
            CreatePayment(user, plan, "completed", today.AddDays(-31), 100)
        };
        context.SubscriptionPayments.AddRange(payments);
        await context.SaveChangesAsync();
        var query = new AdminBillingQueryService(context);

        var firstPage = await query.GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            Status = "success",
            PeriodDays = 1,
            Page = 0,
            PageSize = 2,
            Sort = "amount",
            Descending = true
        });
        var lastPage = await query.GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            Status = "success",
            PeriodDays = 1,
            Page = 99,
            PageSize = 2,
            Sort = "amount",
            Descending = true
        });
        var failedActivation = await query.GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            Status = "activationfailed",
            PeriodDays = 0
        });
        var failed = await query.GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            Status = "failed",
            PeriodDays = 0
        });
        var unknown = await query.GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            Status = "unknown",
            PeriodDays = 0
        });
        var sevenDays = await query.GetPaymentsPageAsync(new AdminBillingPageQuery { PeriodDays = 7 });
        var allTime = await query.GetPaymentsPageAsync(new AdminBillingPageQuery { PeriodDays = 0, PageSize = 500 });
        var byId = await query.GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            PeriodDays = 0,
            Search = payments[0].Id.ToString(CultureInfo.InvariantCulture)
        });
        var byEmail = await query.GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            PeriodDays = 0,
            Search = user.Email
        });
        var byPhone = await query.GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            PeriodDays = 0,
            Search = user.PhoneNumber
        });

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal([payments[0].Id, payments[1].Id], firstPage.Items.Select(payment => payment.Id));
        Assert.Equal(2, lastPage.Page);
        Assert.Equal([payments[2].Id], lastPage.Items.Select(payment => payment.Id));
        Assert.Equal(payments[7].Id, Assert.Single(failedActivation.Items).Id);
        Assert.Equal(3, failed.TotalCount);
        Assert.Equal(payments[9].Id, Assert.Single(unknown.Items).Id);
        Assert.Equal(10, sevenDays.TotalCount);
        Assert.Equal(12, allTime.TotalCount);
        Assert.Equal(100, allTime.PageSize);
        Assert.Equal(1, allTime.Page);
        Assert.Equal(payments[0].Id, Assert.Single(byId.Items).Id);
        Assert.Equal(12, byEmail.TotalCount);
        Assert.Equal(12, byPhone.TotalCount);
    }

    [Fact]
    public async Task OrganizationSubscriptionsFilterAndSortOnlyOrganizationRows()
    {
        await using var context = fixture.CreateContext();
        var plan = await fixture.CreatePlanAsync(context, "План организаций");
        var active = await fixture.CreateOrganizationAsync(context, "alpha-owner");
        active.Name = "Alpha Детейлинг";
        active.OwnerUser.Email = "alpha-owner@example.test";
        active.OwnerUser.PhoneNumber = "+79990002233";
        var expired = await fixture.CreateOrganizationAsync(context, "beta-owner");
        expired.Name = "Beta СТО";
        expired.OwnerUser.Email = "beta-owner@example.test";
        var future = await fixture.CreateOrganizationAsync(context, "gamma-owner");
        future.Name = "Gamma Автопарк";
        future.OwnerUser.Email = "gamma-owner@example.test";
        var now = DateTime.UtcNow;
        var activeSubscription = new OrganizationSubscription
        {
            OrganizationId = active.Id,
            SubscriptionPlanId = plan.Id,
            SourceCode = "manual",
            StartDate = now.AddDays(-10),
            EndDate = now.AddDays(10)
        };
        context.OrganizationSubscriptions.AddRange(
            activeSubscription,
            new OrganizationSubscription
            {
                OrganizationId = expired.Id,
                SubscriptionPlanId = plan.Id,
                SourceCode = "payment",
                StartDate = now.AddDays(-20),
                EndDate = now.AddDays(-1)
            },
            new OrganizationSubscription
            {
                OrganizationId = future.Id,
                SubscriptionPlanId = plan.Id,
                SourceCode = "manual",
                StartDate = now.AddDays(1),
                EndDate = now.AddDays(20)
            });
        await context.SaveChangesAsync();
        var query = new AdminBillingQueryService(context);

        var activePage = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "active",
            PageSize = 1,
            Sort = "organization"
        });
        var allFirstPage = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "all",
            PageSize = 1,
            Sort = "organization"
        });
        var searchedPage = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "all",
            Search = "beta",
            PageSize = 10,
            Sort = "source"
        });
        var inactivePage = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "inactive",
            PageSize = 10,
            Sort = "end",
            Descending = true
        });
        var expiredPage = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "expired",
            PageSize = 10,
            Sort = "end",
            Descending = true
        });
        var scheduledPage = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "scheduled",
            PageSize = 10,
            Sort = "start"
        });
        var activeById = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "all",
            Search = activeSubscription.Id.ToString(CultureInfo.InvariantCulture)
        });
        var activeByEmail = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "all",
            Search = "alpha-owner@example.test"
        });
        var activeByPhone = await query.GetOrganizationSubscriptionsPageAsync(new AdminBillingPageQuery
        {
            Status = "all",
            Search = "+79990002233"
        });

        Assert.Equal(1, activePage.TotalCount);
        Assert.Equal("Alpha Детейлинг", Assert.Single(activePage.Items).OrganizationName);
        Assert.Equal(3, allFirstPage.TotalCount);
        Assert.Equal("Alpha Детейлинг", Assert.Single(allFirstPage.Items).OrganizationName);
        Assert.Equal("Beta СТО", Assert.Single(searchedPage.Items).OrganizationName);
        Assert.Equal(2, inactivePage.TotalCount);
        Assert.Equal("Gamma Автопарк", inactivePage.Items[0].OrganizationName);
        Assert.All(inactivePage.Items, subscription => Assert.False(subscription.IsActive));
        Assert.Equal("Beta СТО", Assert.Single(expiredPage.Items).OrganizationName);
        Assert.Equal("Gamma Автопарк", Assert.Single(scheduledPage.Items).OrganizationName);
        Assert.Equal(activeSubscription.Id, Assert.Single(activeById.Items).Id);
        Assert.Equal(activeSubscription.Id, Assert.Single(activeByEmail.Items).Id);
        Assert.Equal(activeSubscription.Id, Assert.Single(activeByPhone.Items).Id);
    }

    [Fact]
    public async Task PaymentsPageCanReachRecordsBeyondTheFormerLimit()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "pagination-limit@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "План без ограничения списка");
        var payments = Enumerable.Range(0, 305)
            .Select(index => CreatePayment(
                user,
                plan,
                "completed",
                DateTime.UtcNow.AddMinutes(-index),
                100))
            .ToArray();
        context.SubscriptionPayments.AddRange(payments);
        await context.SaveChangesAsync();

        var page = await new AdminBillingQueryService(context).GetPaymentsPageAsync(new AdminBillingPageQuery
        {
            PeriodDays = 0,
            Page = 4,
            PageSize = 100
        });

        Assert.Equal(305, page.TotalCount);
        Assert.Equal(4, page.Page);
        Assert.Equal(100, page.PageSize);
        Assert.Equal(5, page.Items.Count);
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static SubscriptionPayment CreatePayment(
        ApplicationUser user,
        SubscriptionPlan plan,
        string status,
        DateTime createdAt,
        decimal amount,
        string? transactionId = null,
        DateTime? paidAt = null)
    {
        return new SubscriptionPayment
        {
            User = user,
            SubscriptionPlan = plan,
            PaymentStatus = status,
            CreatedAt = createdAt,
            Amount = amount,
            TransactionId = transactionId,
            PaidAt = paidAt
        };
    }
}
