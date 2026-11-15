using Helpers;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Workspace.Shell;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class WorkspaceShellTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task GlobalAssistantSwitchControlsVisibilityForFreeWithoutGrantingPaidAccess(
        bool enabled, bool webEnabled, bool expectedVisible)
    {
        await using var context = fixture.CreateContext();
        var settings = await context.Set<AssistantSettings>().SingleOrDefaultAsync(item => item.Id == 1);
        if (settings == null)
        {
            settings = new AssistantSettings();
            context.Add(settings);
        }

        settings.IsEnabled = enabled;
        settings.WebEnabled = webEnabled;
        await context.SaveChangesAsync();
        var organizationId = Guid.NewGuid();
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetByIdAsync("actor", organizationId))
            .ReturnsAsync(new OrganizationDto { Id = organizationId, PrimaryScenarioCode = "detailing" });

        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetFeatureSnapshotAsync("actor", organizationId))
            .ReturnsAsync(WorkspaceShellTests.CreateFeatures(false));

        var service = new OrganizationWorkspaceShellService(organizations.Object, entitlement.Object,
            Mock.Of<IOrganizationPermissionService>(), Mock.Of<IOrganizationClientCountQueryService>(),
            WorkspaceShellTests.CreateWikiAvailability(true), WorkspaceShellTests.CreateCommercialPolicy(true), context);

        var shell = await service.GetAsync("actor", organizationId);

        Assert.Equal(expectedVisible, shell.Scenario.Modules.AssistantVisible);
        Assert.False(shell.Scenario.Modules.Assistant);
    }

    [Theory]
    [InlineData("detailing", "Детейлинг", "Записать услугу")]
    [InlineData("wrapping", "Оклейка и тюнинг", "Создать обслуживание")]
    [InlineData("fleet", "Автопарк", "Создать обслуживание")]
    [InlineData("driving-school", "Автошкола", "Создать обслуживание")]
    [InlineData("service-station", "Автосервис", "Новый заказ-наряд")]
    [InlineData(" SERVICE-STATION ", "Автосервис", "Новый заказ-наряд")]
    [InlineData("other", "Автосервис", "Создать обслуживание")]
    [InlineData(null, "Автосервис", "Новый заказ-наряд")]
    [InlineData(" ", "Автосервис", "Новый заказ-наряд")]
    public async Task ScenarioTermsFormsMenuAndActionsKeepTheirPublicValues(
        string? scenario, string title, string createTitle)
    {
        await using var context = fixture.CreateContext();
        var organizationId = Guid.NewGuid();
        var organization = new OrganizationDto { Id = organizationId, PrimaryScenarioCode = scenario! };
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetByIdAsync("actor", organizationId)).ReturnsAsync(organization);
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetFeatureSnapshotAsync("actor", organizationId))
            .ReturnsAsync(WorkspaceShellTests.CreateFeatures(true));
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync("actor", organizationId, It.IsAny<string>())).ReturnsAsync(true);
        var clients = new Mock<IOrganizationClientCountQueryService>();
        clients.Setup(service => service.GetActiveCountAsync(organizationId)).ReturnsAsync(17);
        var service = new OrganizationWorkspaceShellService(organizations.Object, entitlement.Object,
            permissions.Object, clients.Object, WorkspaceShellTests.CreateWikiAvailability(true),
            WorkspaceShellTests.CreateCommercialPolicy(true), context);

        var shell = await service.GetAsync("actor", organizationId);

        Assert.Same(organization, shell.Organization);
        Assert.DoesNotContain(shell.Menu, item => item.Code == "maintenance");
        Assert.Equal(title, shell.Scenario.Title);
        Assert.Equal(createTitle, shell.QuickActions.Single(action => action.Code == "create-maintenance").Title);
        Assert.All(shell.QuickActions, action => Assert.True(action.IsVisible));
        Assert.Equal(17, shell.Menu.Single(item => item.Code == "clients").Count);
        Assert.True(shell.Menu.Single(item => item.Code == "settings").IsVisible);
        Assert.False(shell.Menu.Single(item => item.Code == "my-work").IsVisible);
        Assert.NotEmpty(shell.Scenario.Capabilities);
        Assert.NotEmpty(shell.Scenario.RecommendedModules);
        Assert.NotEmpty(shell.Scenario.Focus);
        Assert.Equal(4, shell.Scenario.Metrics.Count);
        Assert.Empty(shell.Scenario.DashboardHint);
        Assert.True(shell.Scenario.MaintenanceForm.ShowPhotos);
        Assert.True(shell.Scenario.MaintenanceForm.ShowDocuments);
        Assert.True(shell.Scenario.MaintenanceForm.ShowMaterials);
        Assert.Equal(scenario == "detailing", shell.Scenario.MaintenanceForm.Mode == "quick-service");
        var expectedCode = string.IsNullOrWhiteSpace(scenario) ? "service-station" : scenario.Trim().ToLowerInvariant();
        Assert.Equal(expectedCode, shell.Scenario.Code);
        Assert.Equal(expectedCode, shell.Scenario.SourceCode);

        if (expectedCode == "detailing")
        {
            Assert.Equal(
                new[]
                {
                    ("workspace", "Ежедневная работа"),
                    ("scheduler", "Ежедневная работа"),
                    ("work-journal", "Ежедневная работа"),
                    ("visualization", "Ежедневная работа"),
                    ("my-work", "Ежедневная работа"),
                    ("messages", "Клиенты"),
                    ("inquiries", "Клиенты"),
                    ("clients", "Клиенты"),
                    ("vehicles", "Клиенты"),
                    ("materials", "Ресурсы"),
                    ("employees", "Ресурсы"),
                    ("shifts", "Ресурсы"),
                    ("members", "Ресурсы"),
                    ("catalog", "Ресурсы"),
                    ("finance", "Отчёты"),
                    ("statistics", "Отчёты"),
                    ("wiki", "Справка и доступ"),
                    ("billing", "Справка и доступ"),
                    ("settings", "Справка и доступ")
                },
                shell.Menu.Select(item => (item.Code, item.GroupTitle)));
        }

        if (expectedCode == "service-station")
        {
            Assert.Equal(
                new[]
                {
                    ("workspace", "Ежедневная работа"),
                    ("scheduler", "Ежедневная работа"),
                    ("work-journal", "Ежедневная работа"),
                    ("visualization", "Ежедневная работа"),
                    ("my-work", "Ежедневная работа"),
                    ("messages", "Клиенты"),
                    ("inquiries", "Клиенты"),
                    ("clients", "Клиенты"),
                    ("vehicles", "Клиенты"),
                    ("materials", "Ресурсы"),
                    ("employees", "Ресурсы"),
                    ("shifts", "Ресурсы"),
                    ("members", "Ресурсы"),
                    ("catalog", "Ресурсы"),
                    ("finance", "Отчёты"),
                    ("statistics", "Отчёты"),
                    ("wiki", "Справка и доступ"),
                    ("billing", "Справка и доступ"),
                    ("settings", "Справка и доступ")
                },
                shell.Menu.Select(item => (item.Code, item.GroupTitle)));
        }

        if (expectedCode == "fleet")
        {
            Assert.Equal(
                new[]
                {
                    "workspace", "visualization", "vehicles", "clients", "defects", "counters", "scheduler",
                    "materials", "work-journal", "finance", "statistics", "my-work", "wiki", "employees",
                    "shifts", "members", "billing", "catalog", "settings"
                },
                shell.Menu.Select(item => item.Code));
            Assert.All(shell.Menu.Where(item => item.Code is "workspace" or "visualization"),
                item => Assert.Equal("Основное", item.GroupTitle));
            Assert.All(shell.Menu.Where(item => item.Code is "finance" or "statistics"),
                item => Assert.Equal("Отчеты", item.GroupTitle));
        }
    }

    [Theory]
    [InlineData(false, true, false, true, false, true)]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(true, true, false, false, true, true)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, true, true, true, true)]
    public async Task WikiMenuRequiresContentAndPermissionAndKeepsCommercialFeatureGate(
        bool hasContent,
        bool canViewWiki,
        bool commercialEnabled,
        bool wikiFeatureEnabled,
        bool expectedVisible,
        bool expectedAvailable)
    {
        await using var context = fixture.CreateContext();
        var organizationId = Guid.NewGuid();
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetByIdAsync("actor", organizationId))
            .ReturnsAsync(new OrganizationDto { Id = organizationId, PrimaryScenarioCode = OrganizationTypeCodes.Detailing });
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetFeatureSnapshotAsync("actor", organizationId))
            .ReturnsAsync(WorkspaceShellTests.CreateFeatures(wikiFeatureEnabled));
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync("actor", organizationId, It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string permissionCode) =>
                permissionCode != OrganizationPermissionCodes.WikiView || canViewWiki);
        var clients = new Mock<IOrganizationClientCountQueryService>();
        clients.Setup(service => service.GetActiveCountAsync(organizationId)).ReturnsAsync(0);
        var service = new OrganizationWorkspaceShellService(organizations.Object, entitlement.Object,
            permissions.Object, clients.Object, WorkspaceShellTests.CreateWikiAvailability(hasContent),
            WorkspaceShellTests.CreateCommercialPolicy(commercialEnabled), context);

        var shell = await service.GetAsync("actor", organizationId);

        var wiki = shell.Menu.Single(item => item.Code == "wiki");
        Assert.Equal(expectedVisible, wiki.IsVisible);
        Assert.Equal(expectedAvailable, wiki.IsAvailable);
        Assert.Equal(commercialEnabled ? FeatureCodes.WikiModule : null, wiki.RequiredFeatureCode);
        Assert.Equal(hasContent && (!commercialEnabled || wikiFeatureEnabled), shell.Scenario.Modules.Wiki);
        Assert.Equal(commercialEnabled, shell.Menu.Single(item => item.Code == "billing").IsVisible);
        Assert.Equal(commercialEnabled && wikiFeatureEnabled, shell.Scenario.Modules.Billing);
    }

    [Theory]
    [MemberData(nameof(WorkspaceShellTests.MenuCases))]
    public async Task MenuRequiresFeatureAndPermissionAndOwnWorkRequiresActiveLinkedEmployee(
        string scenario, bool featuresEnabled, bool permissionEnabled, string permissionSet)
    {
        await using var context = fixture.CreateContext();
        var actor = new ApplicationUser();
        var organization = new Organization { Name = "Menu permissions", OwnerUser = actor };
        var member = new OrganizationMember { User = actor, RoleCode = OrganizationRoleCodes.Employee };
        organization.Members.Add(member);
        context.Organizations.Add(organization);
        context.OrganizationEmployees.AddRange(
            new OrganizationEmployee { Organization = organization, Name = "Active", Member = member, IsActive = permissionSet == "all" },
            new OrganizationEmployee { Organization = organization, Name = "Unlinked", IsActive = true });
        await context.SaveChangesAsync();
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetByIdAsync(actor.Id, organization.Id))
            .ReturnsAsync(new OrganizationDto { Id = organization.Id, PrimaryScenarioCode = scenario });
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetFeatureSnapshotAsync(actor.Id, organization.Id))
            .ReturnsAsync(WorkspaceShellTests.CreateFeatures(featuresEnabled));
        var granted = permissionSet switch
        {
            "secondary" => new HashSet<string>
            {
                OrganizationPermissionCodes.FinanceManage, OrganizationPermissionCodes.MaintenanceEdit,
                OrganizationPermissionCodes.WorkspaceRequisitesEdit
            },
            "archive" => new HashSet<string> { OrganizationPermissionCodes.WorkspaceArchive },
            "members" => new HashSet<string> { OrganizationPermissionCodes.MembersView },
            "inquiries-only" => new HashSet<string> { OrganizationPermissionCodes.InquiriesView },
            "defects" => new HashSet<string> { OrganizationPermissionCodes.WorkJournalView, OrganizationPermissionCodes.DefectsView },
            "maintenance-only" => new HashSet<string> { OrganizationPermissionCodes.WorkJournalView, OrganizationPermissionCodes.MaintenanceView },
            "journal-only" => new HashSet<string> { OrganizationPermissionCodes.WorkJournalView },
            "work-only" => new HashSet<string> { OrganizationPermissionCodes.MaintenanceWorkView },
            _ => new HashSet<string>()
        };
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(actor.Id, organization.Id, It.IsAny<string>()))
            .ReturnsAsync((string userId, Guid organizationId, string code) => permissionEnabled && (permissionSet == "all" || granted.Contains(code)));
        var clients = new Mock<IOrganizationClientCountQueryService>();
        clients.Setup(service => service.GetActiveCountAsync(organization.Id)).ReturnsAsync(5);
        var service = new OrganizationWorkspaceShellService(organizations.Object, entitlement.Object,
            permissions.Object, clients.Object, WorkspaceShellTests.CreateWikiAvailability(true),
            WorkspaceShellTests.CreateCommercialPolicy(true), context);

        var shell = await service.GetAsync(actor.Id, organization.Id);

        var fullAccess = featuresEnabled && permissionEnabled && permissionSet == "all";
        if (scenario == OrganizationTypeCodes.Fleet)
        {
            Assert.DoesNotContain(shell.Menu, item => item.Code == "inquiries");
        }
        else
        {
            var canViewInquiries = permissionEnabled && permissionSet is "all" or "inquiries-only";
            Assert.Equal(canViewInquiries, shell.Menu.Single(item => item.Code == "inquiries").IsVisible);
            var messages = shell.Menu.Single(item => item.Code == "messages");
            Assert.Equal(FeatureCodes.ClientMessaging, messages.RequiredFeatureCode);
            Assert.Equal(featuresEnabled, messages.IsAvailable);
        }

        var canViewClients = permissionEnabled && permissionSet == "all";
        Assert.Equal(canViewClients ? (int?)5 : null, shell.Menu.Single(item => item.Code == "clients").Count);
        Assert.Equal(permissionEnabled && permissionSet == "all", shell.Menu.Single(item => item.Code == "my-work").IsVisible);
        Assert.Equal(permissionEnabled && permissionSet is "all" or "secondary" or "archive",
            shell.Menu.Single(item => item.Code == "settings").IsVisible);
        Assert.Equal(permissionEnabled && permissionSet is "all" or "secondary",
            shell.Menu.Single(item => item.Code == "finance").IsVisible);
        Assert.Equal(permissionEnabled && permissionSet is "all" or "secondary" or "members",
            shell.Menu.Single(item => item.Code == "employees").IsVisible);
        Assert.Equal(permissionEnabled && permissionSet is "all" or "defects" or "maintenance-only",
            shell.Menu.Single(item => item.Code == "work-journal").IsVisible);
        Assert.All(shell.QuickActions, action => Assert.Equal(fullAccess, action.IsVisible));
        Assert.All(shell.Menu.Where(item => item.RequiredFeatureCode is not null), item =>
        {
            Assert.Equal(featuresEnabled, item.IsAvailable);
            Assert.Equal(featuresEnabled ? null : "Доступно в PRO", item.UnavailableMessage);
        });
        clients.Verify(service => service.GetActiveCountAsync(organization.Id), canViewClients ? Times.Once() : Times.Never());
    }

    public static IEnumerable<object[]> MenuCases()
    {
        var permissionCases = new (bool Features, bool Permissions, string Set)[]
        {
            (false, false, "none"),
            (false, true, "all"),
            (true, false, "none"),
            (true, true, "all"),
            (true, true, "secondary"),
            (true, true, "archive"),
            (true, true, "members"),
            (true, true, "inquiries-only"),
            (true, true, "defects"),
            (true, true, "maintenance-only"),
            (true, true, "journal-only"),
            (true, true, "work-only")
        };
        foreach (var scenario in new[] { "detailing", "service-station", "fleet" })
        {
            foreach (var permissions in permissionCases)
            {
                yield return [scenario, permissions.Features, permissions.Permissions, permissions.Set];
            }
        }
    }

    private static FeatureAccessSnapshotDto CreateFeatures(bool enabled) => new()
    {
        Features = new[]
        {
            FeatureCodes.VehiclesModule, FeatureCodes.ClientsModule, FeatureCodes.MaintenanceModule,
            FeatureCodes.VehiclePhotoAlbums, FeatureCodes.MaintenanceIntake, FeatureCodes.Assistant, FeatureCodes.ClientMessaging,
            FeatureCodes.DefectsModule, FeatureCodes.CountersModule, FeatureCodes.SchedulerModule,
            FeatureCodes.MaterialsModule, FeatureCodes.DocumentsModule, FeatureCodes.WorkJournalModule,
            FeatureCodes.FinanceModule, FeatureCodes.AnalyticsModule, FeatureCodes.WikiModule,
            FeatureCodes.MembersModule, FeatureCodes.BillingModule, FeatureCodes.SettingsModule
        }.Select(code => new FeatureValueDto { Code = code, BoolValue = enabled })
            .Append(new FeatureValueDto { Code = "unknown", BoolValue = null }).ToList()
    };

    private static IOrganizationWikiAvailability CreateWikiAvailability(bool hasContent)
    {
        var availability = new Mock<IOrganizationWikiAvailability>();
        availability.Setup(service => service.HasContent).Returns(hasContent);

        return availability.Object;
    }

    [Fact]
    public async Task NeutralModeReportsDisabledCapabilitiesWithoutOfferingPro()
    {
        await using var context = fixture.CreateContext();
        var organizationId = Guid.NewGuid();
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetByIdAsync("actor", organizationId))
            .ReturnsAsync(new OrganizationDto { Id = organizationId, PrimaryScenarioCode = OrganizationTypeCodes.Detailing });
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetFeatureSnapshotAsync("actor", organizationId))
            .ReturnsAsync(WorkspaceShellTests.CreateFeatures(false));
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync("actor", organizationId, It.IsAny<string>())).ReturnsAsync(true);
        var service = new OrganizationWorkspaceShellService(organizations.Object, entitlement.Object,
            permissions.Object, Mock.Of<IOrganizationClientCountQueryService>(),
            WorkspaceShellTests.CreateWikiAvailability(false), WorkspaceShellTests.CreateCommercialPolicy(false), context);

        var shell = await service.GetAsync("actor", organizationId);

        var messages = shell.Menu.Single(item => item.Code == "messages");
        Assert.True(messages.IsVisible);
        Assert.False(messages.IsAvailable);
        Assert.Equal("Функция отключена в настройках установки", messages.UnavailableMessage);
        Assert.False(shell.Menu.Single(item => item.Code == "billing").IsVisible);
    }

    private static IPlatformCommercialPolicy CreateCommercialPolicy(bool enabled)
    {
        var policy = new Mock<IPlatformCommercialPolicy>();
        policy.Setup(service => service.CommercialEnabled).Returns(enabled);

        return policy.Object;
    }
}
