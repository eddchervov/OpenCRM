using OpenCRM.Modules.Planning.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Planning.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;
using ISystemClock = OpenCRM.Domain.Interfaces.ISystemClock;

using OpenCRM.Modules.Planning;
namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class PlanningHttpAcceptanceTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public async Task CalendarAndRemindersRequireAuthenticationAndOrganizationMembership()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreign = await fixture.CreateOrganizationAsync(context);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        var route = $"/api/v1/organizations/{organization.Id}/workspace/";
        using var anonymous = await client.GetAsync(route + "scheduler-tasks-calendar");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", foreign.OwnerUserId);
        using var denied = await client.GetAsync(route + "scheduler-reminders");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var calendar = await client.GetAsync(route + "scheduler-tasks-calendar?dateFrom=2026-10-01&dateTo=2026-10-31");
        Assert.Equal(HttpStatusCode.OK, calendar.StatusCode);
        Assert.Equal("[]", await calendar.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExistingHttpRoutesRescheduleReadAndMarkRemindersThroughPlanningPorts()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var now = DateTime.UtcNow;
        var task = new OrganizationSchedulerTask
        {
            OrganizationId = organization.Id,
            CreatedByUserId = organization.OwnerUserId,
            Title = "Позвонить клиенту",
            StartAt = now.Date.AddHours(10),
            EndAt = now.Date.AddHours(11),
            ReminderAt = now.AddMinutes(-5)
        };
        context.OrganizationSchedulerTasks.Add(task);
        await context.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        var route = $"/api/v1/organizations/{organization.Id}/workspace/";
        using var reminders = await client.GetAsync(route + "scheduler-reminders");
        Assert.Equal(HttpStatusCode.OK, reminders.StatusCode);
        using var reminderJson = JsonDocument.Parse(await reminders.Content.ReadAsStringAsync());
        var reminder = Assert.Single(reminderJson.RootElement.EnumerateArray());
        Assert.Equal(task.Id, reminder.GetProperty("taskId").GetGuid());
        Assert.Equal("Позвонить клиенту", reminder.GetProperty("title").GetString());
        Assert.Equal($"#scheduler?task={task.Id}", reminder.GetProperty("detailUrl").GetString());
        var nextStart = now.Date.AddDays(2).AddHours(14);
        using var rescheduled = await client.PutAsJsonAsync(route + $"scheduler-tasks/{task.Id}/schedule",
            new UpdateOrganizationSchedulerTaskScheduleDto { StartAt = nextStart, EndAt = nextStart.AddHours(2) });
        Assert.Equal(HttpStatusCode.NoContent, rescheduled.StatusCode);
        using var marked = await client.PostAsync(route + $"scheduler-reminders/{task.Id}/sent", null);
        Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);
        using var remaining = await client.GetAsync(route + "scheduler-reminders");
        Assert.Equal("[]", await remaining.Content.ReadAsStringAsync());
        await using var verification = fixture.CreateContext();
        var saved = await verification.OrganizationSchedulerTasks.AsNoTracking().SingleAsync(value => value.Id == task.Id);
        Assert.Equal(nextStart, saved.StartAt);
        Assert.Equal(nextStart.AddHours(2), saved.EndAt);
        Assert.NotNull(saved.ReminderSentAt);
        using var deniedHost = this.CreateHost(canWrite: false);
        using var deniedClient = deniedHost.GetTestClient();
        deniedClient.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var denied = await deniedClient.PutAsJsonAsync(route + $"scheduler-tasks/{task.Id}/schedule",
            new UpdateOrganizationSchedulerTaskScheduleDto { StartAt = nextStart.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await verification.Entry(saved).ReloadAsync();
        Assert.Equal(nextStart, saved.StartAt);
    }

    private IHost CreateHost(bool canWrite = true)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
                services.AddScoped<UserManager<ApplicationUser>>(provider =>
                {
                    var context = provider.GetRequiredService<OpenCRMContext>();
                    var manager = new Mock<UserManager<ApplicationUser>>(
                        Mock.Of<IUserStore<ApplicationUser>>(),
                        Options.Create(new IdentityOptions()),
                        Mock.Of<IPasswordHasher<ApplicationUser>>(),
                        Array.Empty<IUserValidator<ApplicationUser>>(),
                        Array.Empty<IPasswordValidator<ApplicationUser>>(),
                        Mock.Of<ILookupNormalizer>(),
                        new IdentityErrorDescriber(),
                        Mock.Of<IServiceProvider>(),
                        Mock.Of<ILogger<UserManager<ApplicationUser>>>());
                    manager.Setup(value => value.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                        .Returns<ClaimsPrincipal>(principal =>
                        {
                            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                            return userId == null
                                ? Task.FromResult<ApplicationUser?>(null)
                                : context.Users.SingleOrDefaultAsync(user => user.Id == userId);
                        });

                    return manager.Object;
                });
                services.AddScoped<IOrganizationQueryService>(provider =>
                {
                    var context = provider.GetRequiredService<OpenCRMContext>();
                    var query = new Mock<IOrganizationQueryService>();
                    query.Setup(value => value.GetOverviewAsync(It.IsAny<string>()))
                        .Returns<string>(GetOverviewAsync);
                    query.Setup(value => value.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                        .Returns<string, Guid>(GetOrganizationAsync);

                    return query.Object;

                    async Task<TeamOverviewDto> GetOverviewAsync(string userId)
                    {
                        var organizations = await context.Organizations.AsNoTracking()
                            .Where(organization => organization.Members.Any(member => member.UserId == userId)
                                && !organization.IsDeleted && !organization.IsArchived && organization.IsActive)
                            .Select(organization => new OrganizationDto { Id = organization.Id, Name = organization.Name })
                            .ToListAsync();

                        return new TeamOverviewDto { Organizations = organizations };
                    }

                    async Task<OrganizationDto> GetOrganizationAsync(string userId, Guid organizationId)
                    {
                        return await context.Organizations.AsNoTracking()
                            .Where(organization => organization.Id == organizationId
                                && organization.Members.Any(member => member.UserId == userId)
                                && !organization.IsDeleted && !organization.IsArchived && organization.IsActive)
                            .Select(organization => new OrganizationDto
                            {
                                Id = organization.Id,
                                Name = organization.Name,
                                TimeZoneId = organization.ShiftTimeZoneId
                            })
                            .FirstAsync();
                    }
                });
                services.AddScoped<IOrganizationSubscriptionCheckoutService>(_ => Mock.Of<IOrganizationSubscriptionCheckoutService>());
                services.AddScoped<IOrganizationSubscriptionService>(provider =>
                {
                    var context = provider.GetRequiredService<OpenCRMContext>();
                    var subscription = new Mock<IOrganizationSubscriptionService>();
                    subscription.Setup(value => value.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                        .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMembershipAsync(userId, organizationId));
                    subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                        .Returns<string, Guid>(EnsureMembershipAsync);
                    subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                        .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMembershipAsync(userId, organizationId));

                    subscription.Setup(value => value.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
                    subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns<string, Guid, string>(async (userId, organizationId, _) =>
                    {
                        await EnsureMembershipAsync(userId, organizationId);
                        if (!canWrite)
                        {
                            throw new InvalidOperationException("Недостаточно прав.");
                        }
                    });

                    return subscription.Object;

                    async Task EnsureMembershipAsync(string userId, Guid organizationId)
                    {
                        if (!await context.OrganizationMembers.AsNoTracking().AnyAsync(member =>
                            member.OrganizationId == organizationId && member.UserId == userId))
                        {
                            throw new InvalidOperationException("Нет доступа к организации.");
                        }
                    }
                });
                var permissions = new Mock<IOrganizationPermissionService>();
                permissions.Setup(value => value.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                    .ReturnsAsync(canWrite);
                services.AddSingleton(permissions.Object);
                services.AddSingleton(Mock.Of<IOrganizationProfileCommandService>());
                services.AddSingleton(Mock.Of<IOrganizationArchiveService>());
                services.AddSingleton(Mock.Of<IOrganizationMembershipService>());
                services.AddSingleton(Mock.Of<IOrganizationInvitationCommandService>());
                services.AddSingleton(Mock.Of<IWorkspaceOverviewService>());
                services.AddSingleton(Mock.Of<IOrganizationWorkspaceShellService>());
                services.AddSingleton(Mock.Of<IOrganizationWorkspaceQueryService>());
                services.AddSingleton(Mock.Of<IVehicleOptionQueryService>());

            
                services.AddSingleton(Mock.Of<IInventoryQueryService>());
                services.AddSingleton(Mock.Of<IStockCommandService>());
                services.AddSingleton(Mock.Of<ISystemClock>());
                // Справка организации вынесена из платформы: контроллер рабочего пространства получает статьи справки
                // через этот порт, в приложении его регистрирует слой представления (пустая реализация без статей).
                services.AddSingleton(Mock.Of<OpenCRM.WebApp.Models.Organizations.IOrganizationWikiContentProvider>());
                services.AddOpenCRMCatalog();
                services.AddOpenCRMPlanning();
                services.AddAuthentication("CatalogTest")
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("CatalogTest", _ => { });
                services.AddAuthorization();
                services.AddControllers().AddApplicationPart(typeof(OrganizationWorkspaceApiController).Assembly);
            });
            web.Configure(application =>
            {
                application.UseRouting();
                application.UseAuthentication();
                application.UseAuthorization();
                application.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        });
        var host = builder.Build();
        host.Start();

        return host;
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = this.Request.Headers["X-Test-User"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], this.Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
