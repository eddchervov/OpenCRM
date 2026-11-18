using Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationProfileTests(OrganizationsDatabaseFixture fixture)
{
    [Theory]
    [InlineData("detailing", "RUB", "Asia/Novosibirsk")]
    [InlineData("detailing", "cny", "Asia/Shanghai")]
    [InlineData("service_station", "USD", "America/New_York")]
    [InlineData("service_station", "EUR", "Europe/Berlin")]
    public async Task CreatePersistsOwnerAndSetupState(string scenario, string currencyCode, string timeZoneId)
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = $"creator-{Guid.NewGuid():N}@example.test" };
        context.ApplicationUsers.Add(owner);
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);
        var id = await services.GetRequiredService<IOrganizationProfileCommandService>().CreateAsync(owner.Id,
            new CreateOrganizationDto { Name = "  Студия  ", Description = "  Описание  ", PrimaryScenarioCode = scenario, CurrencyCode = currencyCode, TimeZoneId = timeZoneId });
        var saved = await OrganizationCommandHarness.ReadAsync(context, id);

        Assert.Equal(currencyCode.ToUpperInvariant(), saved.CurrencyCode);
        Assert.Equal(timeZoneId, saved.ShiftTimeZoneId);
        Assert.Equal("Студия", saved.Name);
        Assert.Equal("Описание", saved.Description);
        Assert.Equal(scenario == "service_station" ? "service-station" : scenario, saved.PrimaryScenarioCode);
        Assert.Equal(owner.Id, saved.OwnerUserId);
        Assert.True(saved.SetupAutoOpenPending);
        var member = Assert.Single(saved.Members);
        Assert.Equal(owner.Id, member.UserId);
        Assert.Equal(owner.Id, member.InvitedByUserId);
        Assert.Equal(OrganizationRoleCodes.Owner, member.RoleCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mars/Olympus")]
    [InlineData("Russian Standard Time")]
    public async Task UnsupportedTimeZoneDoesNotCreateOrganization(string timeZoneId)
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IOrganizationProfileCommandService>()
            .CreateAsync(owner.Id, new CreateOrganizationDto
            {
                Name = "Тестовая организация",
                PrimaryScenarioCode = "detailing",
                TimeZoneId = timeZoneId
            }));

        Assert.False(await context.Organizations.AsNoTracking().AnyAsync(item => item.OwnerUserId == owner.Id));
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("")]
    public async Task UnsupportedCurrencyDoesNotCreateOrganization(string currencyCode)
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IOrganizationProfileCommandService>()
            .CreateAsync(owner.Id, new CreateOrganizationDto
            {
                Name = "Тестовая организация",
                PrimaryScenarioCode = "detailing",
                CurrencyCode = currencyCode
            }));

        Assert.False(await context.Organizations.AsNoTracking().AnyAsync(item => item.OwnerUserId == owner.Id));
    }

    [Fact]
    public async Task ConcurrentCreationOfSameScenarioAllowsOnlyOneOrganizationPerOwner()
    {
        await using var seedContext = fixture.CreateContext();
        var owner = new OpenCRM.Domain.Identity.ApplicationUser { Email = $"creator-{Guid.NewGuid():N}@example.test" };
        seedContext.ApplicationUsers.Add(owner);
        await seedContext.SaveChangesAsync();

        var results = await Task.WhenAll(AttemptCreateAsync(), AttemptCreateAsync());

        Assert.Single(results.Where(organizationId => organizationId.HasValue));
        await using var verifyContext = fixture.CreateContext();
        Assert.Equal(1, await verifyContext.Organizations.CountAsync(organization =>
            organization.OwnerUserId == owner.Id && organization.PrimaryScenarioCode == "detailing" && !organization.IsDeleted));

        async Task<Guid?> AttemptCreateAsync()
        {
            await using var context = fixture.CreateContext();
            using var services = OrganizationCommandHarness.CreateServices(context);
            try
            {
                return await services.GetRequiredService<IOrganizationProfileCommandService>().CreateAsync(owner.Id,
                    new CreateOrganizationDto { Name = "Студия", PrimaryScenarioCode = "detailing" });
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    [Fact]
    public async Task OwnerCanCreateOneOrganizationOfEachScenarioConcurrently()
    {
        await using var seedContext = fixture.CreateContext();
        var owner = new OpenCRM.Domain.Identity.ApplicationUser { Email = $"creator-{Guid.NewGuid():N}@example.test" };
        seedContext.ApplicationUsers.Add(owner);
        await seedContext.SaveChangesAsync();

        var results = await Task.WhenAll(AttemptCreateAsync("detailing"), AttemptCreateAsync("service_station"));

        Assert.Equal(2, results.Distinct().Count());
        await using var verifyContext = fixture.CreateContext();
        var scenarios = await verifyContext.Organizations.AsNoTracking()
            .Where(organization => organization.OwnerUserId == owner.Id && !organization.IsDeleted)
            .Select(organization => organization.PrimaryScenarioCode)
            .ToListAsync();
        Assert.Equal(new[] { "detailing", "service-station" }, scenarios.OrderBy(scenario => scenario).ToArray());

        async Task<Guid> AttemptCreateAsync(string scenario)
        {
            await using var context = fixture.CreateContext();
            using var services = OrganizationCommandHarness.CreateServices(context);
            return await services.GetRequiredService<IOrganizationProfileCommandService>().CreateAsync(owner.Id,
                new CreateOrganizationDto { Name = "Организация", PrimaryScenarioCode = scenario });
        }
    }

    [Fact]
    public async Task ArchivedOrganizationStillUsesOwnersScenarioSlot()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.IsArchived = true;
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => services
            .GetRequiredService<IOrganizationProfileCommandService>().CreateAsync(organization.OwnerUserId,
                new CreateOrganizationDto { Name = "Новая студия", PrimaryScenarioCode = "detailing" }));

        Assert.Contains("в архиве", exception.Message.ToLowerInvariant());
        Assert.Single(await context.Organizations.Where(value => value.OwnerUserId == organization.OwnerUserId).ToListAsync());
    }

    [Fact]
    public async Task DeletedOrganizationDoesNotUseOwnersScenarioSlot()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.IsDeleted = true;
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);

        var createdId = await services.GetRequiredService<IOrganizationProfileCommandService>().CreateAsync(organization.OwnerUserId,
            new CreateOrganizationDto { Name = "Новая студия", PrimaryScenarioCode = "detailing" });

        Assert.NotEqual(organization.Id, createdId);
        Assert.Equal(1, await context.Organizations.CountAsync(value =>
            value.OwnerUserId == organization.OwnerUserId && !value.IsDeleted && value.PrimaryScenarioCode == "detailing"));
    }

    [Fact]
    public async Task MembershipInAnotherOwnersOrganizationDoesNotUseScenarioSlot()
    {
        await using var context = fixture.CreateContext();
        var existingOrganization = await fixture.CreateOrganizationAsync(context);
        var member = new OpenCRM.Domain.Identity.ApplicationUser { Email = $"member-{Guid.NewGuid():N}@example.test" };
        await fixture.AddMemberAsync(context, existingOrganization, member);
        using var services = OrganizationCommandHarness.CreateServices(context);

        var createdId = await services.GetRequiredService<IOrganizationProfileCommandService>().CreateAsync(member.Id,
            new CreateOrganizationDto { Name = "Своя студия", PrimaryScenarioCode = "detailing" });

        var created = await OrganizationCommandHarness.ReadAsync(context, createdId);
        Assert.Equal(member.Id, created.OwnerUserId);
        Assert.Equal(OrganizationRoleCodes.Owner, Assert.Single(created.Members).RoleCode);
    }

    [Theory]
    [InlineData("update", "123", null, null)]
    [InlineData("update", null, "123", null)]
    [InlineData("update", null, null, "123")]
    [InlineData("requisites", "123", null, null)]
    [InlineData("requisites", null, "123", null)]
    [InlineData("requisites", null, null, "123")]
    public async Task InvalidRequisitesDoNotMutateOrganizationOrWriteAudit(string operation, string? inn, string? kpp, string? ogrn)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = OrganizationCommandHarness.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationProfileCommandService>();

        if (operation == "update")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(organization.OwnerUserId, organization.Id,
                new UpdateOrganizationDto { Name = "Не сохранять", PrimaryScenarioCode = "detailing", Inn = inn, Kpp = kpp, Ogrn = ogrn }));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateRequisitesAsync(organization.OwnerUserId, organization.Id,
                new UpdateOrganizationRequisitesDto { LegalName = "Не сохранять", Inn = inn, Kpp = kpp, Ogrn = ogrn }));
        }

        var saved = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        Assert.Equal(organization.Name, saved.Name);
        Assert.Null(saved.LegalName);
        Assert.Null(saved.Inn);
        Assert.Null(saved.Kpp);
        Assert.Null(saved.Ogrn);
        Assert.DoesNotContain(context.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        Assert.False(await context.OrganizationAuditEvents.AnyAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("profile")]
    [InlineData("requisites")]
    public async Task CommandsNormalizeAndPersistOwnAudit(string operation)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), organization.Id, It.IsAny<string>())).ReturnsAsync(true);
        using var services = OrganizationCommandHarness.CreateServices(context, subscriptions: subscriptions.Object);
        var service = services.GetRequiredService<IOrganizationProfileCommandService>();
        if (operation == "update")
        {
            await service.UpdateAsync(organization.OwnerUserId, organization.Id, new UpdateOrganizationDto
            {
                Name = "  Новое имя  ", Description = "  Описание  ", PrimaryScenarioCode = "detailing",
                LegalName = "  Юрлицо  ", Inn = "  1234567890  ", Kpp = "  1234ab123  ", Ogrn = "  1234567890123  ",
                LegalAddress = "  Юридический адрес  ", ActualAddress = "  Фактический адрес  ",
                Phone = "  555  ", Email = "  mail@example.test  ", SignatoryPosition = "  Директор  ",
                SignatoryName = "  Иван  ", SignatoryBasis = "  Устав  ", DocumentStyleCode = "  MINIMAL  ",
                MaintenanceWorkflowMode = "  SIMPLE  "
            });
        }
        else if (operation == "profile")
        {
            await service.UpdateProfileAsync(organization.OwnerUserId, organization.Id, new UpdateOrganizationProfileDto
            {
                Name = "  Новое имя  ", Description = "  Описание  ", PrimaryScenarioCode = "detailing",
                DocumentStyleCode = "  MINIMAL  ", MaintenanceWorkflowMode = "  SIMPLE  ", AllowWorkSelfAssignment = true
            });
        }
        else
        {
            await service.UpdateRequisitesAsync(organization.OwnerUserId, organization.Id, new UpdateOrganizationRequisitesDto
            {
                LegalName = "  Юрлицо  ", Inn = "  1234567890  ", Kpp = "  1234ab123  ", Ogrn = "  1234567890123  ",
                LegalAddress = "  Юридический адрес  ", ActualAddress = "  Фактический адрес  ",
                Phone = "  555  ", Email = "  mail@example.test  ", SignatoryPosition = "  Директор  ",
                SignatoryName = "  Иван  ", SignatoryBasis = "  Устав  "
            });
        }

        var saved = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        if (operation != "requisites")
        {
            Assert.Equal("Новое имя", saved.Name);
            Assert.Equal("Описание", saved.Description);
            Assert.Equal("minimal", saved.DocumentStyleCode);
            Assert.Equal("simple", saved.MaintenanceWorkflowMode);
        }

        if (operation != "profile")
        {
            Assert.Equal("Юрлицо", saved.LegalName);
            Assert.Equal("1234567890", saved.Inn);
            Assert.Equal("1234AB123", saved.Kpp);
            Assert.Equal("1234567890123", saved.Ogrn);
            Assert.Equal("Юридический адрес", saved.LegalAddress);
            Assert.Equal("Фактический адрес", saved.ActualAddress);
            Assert.Equal("555", saved.Phone);
            Assert.Equal("mail@example.test", saved.Email);
            Assert.Equal("Директор", saved.SignatoryPosition);
            Assert.Equal("Иван", saved.SignatoryName);
            Assert.Equal("Устав", saved.SignatoryBasis);
        }

        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync(value => value.OrganizationId == organization.Id);
        Assert.Equal(organization.OwnerUserId, audit.ActorUserId);
        Assert.Equal(organization.Id.ToString("D"), audit.TargetId);
        Assert.Equal("organization", audit.TargetType);
        Assert.NotEqual(Guid.Empty, audit.Id);
    }

    [Theory]
    [InlineData("documents", "profile")]
    [InlineData("documents", "update")]
    [InlineData("assignment", "profile")]
    public async Task PaidSettingsCannotBeChangedWithoutFeature(string setting, string operation)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = OrganizationCommandHarness.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationProfileCommandService>();

        var exception = operation == "update"
            ? await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(organization.OwnerUserId, organization.Id,
                new UpdateOrganizationDto { Name = "Не сохранять", PrimaryScenarioCode = "detailing", DocumentStyleCode = "minimal" }))
            : await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateProfileAsync(organization.OwnerUserId, organization.Id,
                new UpdateOrganizationProfileDto
                {
                    Name = "Не сохранять", PrimaryScenarioCode = "detailing",
                    DocumentStyleCode = setting == "documents" ? "minimal" : "auto",
                    AllowWorkSelfAssignment = setting == "assignment"
                }));

        Assert.Contains("PRO", exception.Message);
        var saved = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        Assert.Equal(organization.Name, saved.Name);
        Assert.Equal("auto", saved.DocumentStyleCode);
        Assert.False(saved.AllowWorkSelfAssignment);
        Assert.False(await context.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData("detailing")]
    [InlineData("service-station")]
    public async Task FreeProfileEditPreservesPreviouslyPaidSettings(string scenario)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.PrimaryScenarioCode = scenario;
        organization.DocumentStyleCode = "minimal";
        organization.AllowWorkSelfAssignment = true;
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);

        await services.GetRequiredService<IOrganizationProfileCommandService>().UpdateProfileAsync(organization.OwnerUserId, organization.Id,
            new UpdateOrganizationProfileDto
            {
                Name = "Новое название", PrimaryScenarioCode = scenario, DocumentStyleCode = " MINIMAL ",
                AllowWorkSelfAssignment = true, MaintenanceWorkflowMode = "simple"
            });

        var saved = await OrganizationCommandHarness.ReadAsync(context, organization.Id);
        Assert.Equal("Новое название", saved.Name);
        Assert.Equal("minimal", saved.DocumentStyleCode);
        Assert.True(saved.AllowWorkSelfAssignment);
        Assert.Equal("simple", saved.MaintenanceWorkflowMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullWorkflowPreservesExistingMode(bool profileOnly)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.MaintenanceWorkflowMode = "simple";
        await context.SaveChangesAsync();
        using var services = OrganizationCommandHarness.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationProfileCommandService>();
        if (profileOnly)
        {
            await service.UpdateProfileAsync(organization.OwnerUserId, organization.Id,
                new UpdateOrganizationProfileDto { Name = organization.Name, PrimaryScenarioCode = "", MaintenanceWorkflowMode = null });
        }
        else
        {
            await service.UpdateAsync(organization.OwnerUserId, organization.Id,
                new UpdateOrganizationDto { Name = organization.Name, PrimaryScenarioCode = "" });
        }

        Assert.Equal("simple", (await OrganizationCommandHarness.ReadAsync(context, organization.Id)).MaintenanceWorkflowMode);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("foreign")]
    [InlineData("name")]
    [InlineData("audit")]
    public async Task FailureDoesNotPersistProfileChanges(string failure)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var previousName = organization.Name;
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        if (failure == "permission")
        {
            subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Нет разрешения."));
        }

        var audit = new Mock<IOrganizationAuditService>();
        audit.Setup(value => value.AddAsync(It.IsAny<OrganizationAuditEventCreateDto>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("Ошибка журнала."));
        using var services = OrganizationCommandHarness.CreateServices(context,
            audit: failure == "audit" ? audit.Object : null, subscriptions: subscriptions.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IOrganizationProfileCommandService>()
            .UpdateProfileAsync(failure == "foreign" ? "foreign" : organization.OwnerUserId, organization.Id,
                new UpdateOrganizationProfileDto { Name = failure == "name" ? " " : "Изменение", PrimaryScenarioCode = "detailing" }));

        Assert.Equal(previousName, (await OrganizationCommandHarness.ReadAsync(context, organization.Id)).Name);
        Assert.False(await context.OrganizationAuditEvents.AsNoTracking().AnyAsync(value => value.OrganizationId == organization.Id));
        Assert.Empty(context.ChangeTracker.Entries());
        await context.SaveChangesAsync();
        Assert.Equal(previousName, (await OrganizationCommandHarness.ReadAsync(context, organization.Id)).Name);
    }
}
