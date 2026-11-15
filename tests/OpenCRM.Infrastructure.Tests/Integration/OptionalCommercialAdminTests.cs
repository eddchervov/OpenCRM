using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class OptionalCommercialAdminTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminReadsRespectModuleStateAndRetainHistoricalSubscriptions(bool enabled)
    {
        await using var setup = fixture.CreateContext();
        var now = DateTime.UtcNow;
        var marker = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { Email = $"optional-{marker}@example.test", NormalizedEmail = $"OPTIONAL-{marker.ToUpperInvariant()}@EXAMPLE.TEST", CanUseTestSubscriptions = true };
        var plan = new SubscriptionPlan { Name = $"Тестовый тариф {marker}", DurationDays = 30 };
        var organization = new Organization
        {
            Name = $"Организация {marker}", OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
        setup.UserSubscriptions.Add(new UserSubscription
        {
            User = user, SubscriptionPlan = plan, StartDate = now.AddDays(-1), EndDate = now.AddDays(20)
        });
        setup.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Organization = organization, SubscriptionPlan = plan, StartDate = now.AddDays(-1), EndDate = now.AddDays(20)
        });
        setup.SubscriptionPayments.Add(new SubscriptionPayment
        {
            User = user, SubscriptionPlan = plan, CreatedAt = now, PaidAt = now, Amount = 175m, PaymentStatus = "completed"
        });
        await setup.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformPolicy:Commercial:Enabled"] = enabled.ToString()
        }).Build();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(setup.Database.GetConnectionString())
            .AddInterceptors(new RejectCommercialReadsInterceptor(!enabled))
            .Options;
        await using var context = new OpenCRMContext(options);
        var users = new AdminUsersService(context, configuration);

        var overview = await users.GetOverviewAsync(user.Id);
        var list = await users.GetAsync(user.Email, UsersSort.ByDateDesc, false, 1, 25, null);
        var organizations = await new AdminOrganizationService(context, configuration).GetByIdAsync(organization.Id);
        var profile = await new AdminCurrentUserService(context, configuration).GetAsync(user.Id);
        var counts = await new AdminNavigationService(context, configuration).GetCountsAsync(
            now.AddSeconds(-1), now.AddSeconds(1), new HashSet<string> { AdminAuthorizationPolicies.ViewBilling });

        Assert.NotNull(overview);
        Assert.NotNull(organizations);
        Assert.NotNull(profile);
        Assert.Equal(enabled, profile.CommercialEnabled);
        Assert.Equal(enabled, overview.Profile.CanUseTestSubscriptions);
        Assert.Equal(enabled, Assert.Single(list.Users).CanUseTestSubscriptions);
        Assert.Equal(enabled ? plan.Name : null, organizations.ActiveSubscriptionName);
        Assert.Equal(enabled ? plan.Name : string.Empty,
            Assert.Single(overview.Organizations, item => item.OrganizationId == organization.Id).CurrentPlanName);
        Assert.Equal(enabled ? 1 : 0, overview.Subscriptions.Users.Count);
        Assert.Equal(enabled ? 1 : 0, overview.Payments.Count);
        Assert.Equal(enabled ? 175m : 0m, overview.Kpis.PaymentsAmount);
        Assert.Equal(enabled ? 1 : 0, counts.Payments);
        Assert.Equal(1, await setup.UserSubscriptions.CountAsync(item => item.UserId == user.Id));
        Assert.Equal(1, await setup.OrganizationSubscriptions.CountAsync(item => item.OrganizationId == organization.Id));
    }

    private sealed class RejectCommercialReadsInterceptor(bool reject) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (reject && new[] { "SubscriptionPlans", "SubscriptionPlanFeatures", "UserSubscriptions", "OrganizationSubscriptions", "SubscriptionPayments", "UserFeatureGrants" }
                .Any(table => command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Базовая админка обратилась к коммерческим таблицам.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
