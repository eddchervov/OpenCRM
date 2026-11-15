using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Features.Dtos;
using Helpers;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientCommandTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task CreatesPersonWithNormalizedFieldsAndDeduplicatedTruncatedTagsInOneSave()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        var clientId = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = "  Анна Иванова  ",
            Phone = " +7 900 123-45-67 ",
            PartyTypeCode = "person",
            Inn = "лишнее значение",
            Comment = " заметка ",
            StatusCode = "LEAD",
            ResponsibleUserId = $" {organization.OwnerUserId} ",
            Notes = " личная заметка ",
            TagNames = ["VIP, vip", new string('д', 52), "  "]
        });

        var client = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == clientId);
        var tags = await context.OrganizationClientTags.AsNoTracking()
            .Where(tag => tag.OrganizationId == organization.Id)
            .OrderBy(tag => tag.Name)
            .ToListAsync();
        var history = await context.OrganizationClientHistoryEvents.AsNoTracking().SingleAsync(item => item.OrganizationClientId == clientId);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync(item => item.TargetId == clientId.ToString("D"));

        Assert.Equal("Анна Иванова", client.Name);
        Assert.Equal("+7 900 123-45-67", client.Phone);
        Assert.Null(client.Inn);
        Assert.Equal("lead", client.StatusCode);
        Assert.Equal("заметка\n\nличная заметка", client.Comment);
        Assert.Equal(organization.OwnerUserId, client.ResponsibleUserId);
        Assert.Equal(2, tags.Count);
        Assert.Equal(48, tags.Single(tag => tag.Name.Length == 48).Name.Length);
        Assert.Equal(1, tags.Count(tag => tag.NormalizedName == "VIP"));
        Assert.Equal("created", history.EventType);
        Assert.Equal("clients.created", audit.EventType);
        Assert.Equal("client", audit.TargetType);
    }

    [Fact]
    public async Task CreatesCompanyWithAccountingFieldsAndDoesNotCreatePersonAccountingData()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        var companyId = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = " ООО Ромашка ",
            PartyTypeCode = "company",
            Inn = " 1234567890 ",
            Kpp = " 123456789 ",
            LegalAddress = " Москва ",
            StatusCode = "unknown"
        });
        var personId = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = "Физическое лицо",
            PartyTypeCode = "person",
            Inn = "1234567890",
            Kpp = "123456789",
            LegalAddress = "Не сохранять"
        });

        var company = await context.OrganizationClients.AsNoTracking().SingleAsync(client => client.Id == companyId);
        var person = await context.OrganizationClients.AsNoTracking().SingleAsync(client => client.Id == personId);

        Assert.Equal("ООО Ромашка", company.Name);
        Assert.Equal("1234567890", company.Inn);
        Assert.Equal("123456789", company.Kpp);
        Assert.Equal("Москва", company.LegalAddress);
        Assert.Equal("active", company.StatusCode);
        Assert.Null(person.Inn);
        Assert.Null(person.Kpp);
        Assert.Null(person.LegalAddress);
    }

    [Theory]
    [InlineData("", "person", "Укажите имя или название клиента.")]
    [InlineData("Клиент", "invalid", "Выберите корректный тип клиента.")]
    [InlineData("Клиент", null, "Выберите корректный тип клиента.")]
    public async Task RejectsMissingNameOrInvalidPartyType(string name, string? partyTypeCode, string expectedMessage)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationClientDto { Name = name, PartyTypeCode = partyTypeCode }));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Empty(await context.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Theory]
    [InlineData("123456789012345678901", null, null)]
    [InlineData(null, "123456789012345678901", null)]
    [InlineData(null, null, "Москва" )]
    public async Task RejectsCompanyAccountingFieldsOverTheirMaximumLength(string? inn, string? kpp, string? address)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationClientDto
            {
                Name = "Компания",
                PartyTypeCode = "company",
                Inn = inn,
                Kpp = kpp,
                LegalAddress = address is null ? address : new string('я', 501)
            }));

        Assert.Contains("не должно превышать", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await context.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task RejectsResponsibleUserWhoIsNotAnOrganizationMember()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new CreateOrganizationClientDto { Name = "Клиент", PartyTypeCode = "person", ResponsibleUserId = "foreign-user" }));

        Assert.Equal("Ответственный сотрудник не найден в организации.", exception.Message);
        Assert.Empty(await context.OrganizationClients.AsNoTracking()
            .Where(client => client.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task RejectsUpdatingMissingArchivedAndForeignClients()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context, "Чужая организация");
        var archivedClient = ClientsDatabaseFixture.CreateClient(organization, "Архивный клиент");
        archivedClient.Delete(DateTime.UtcNow);
        var foreignClient = ClientsDatabaseFixture.CreateClient(foreignOrganization, "Чужой клиент");
        context.OrganizationClients.AddRange(archivedClient, foreignClient);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();
        var update = new UpdateOrganizationClientDto { Name = "Изменение", PartyTypeCode = "person" };

        foreach (var clientId in new[] { Guid.NewGuid(), archivedClient.Id, foreignClient.Id })
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UpdateAsync(
                organization.OwnerUserId, organization.Id, clientId, update));
            Assert.Equal("Клиент не найден.", exception.Message);
        }
    }

    [Fact]
    public async Task UpdatesChangedFieldsTagsAndWritesOneHistoryAndAuditEntry()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherClient = ClientsDatabaseFixture.CreateClient(organization, "Другой клиент");
        var tag = new OrganizationClientTag
        {
            OrganizationId = organization.Id,
            Name = "Существующий",
            NormalizedName = "СУЩЕСТВУЮЩИЙ"
        };
        otherClient.TagAssignments.Add(new OrganizationClientTagAssignment
        {
            OrganizationClient = otherClient,
            OrganizationClientTag = tag
        });
        var client = ClientsDatabaseFixture.CreateClient(organization, "Старое имя");
        client.TagAssignments.Add(new OrganizationClientTagAssignment
        {
            OrganizationClient = client,
            OrganizationClientTag = tag
        });
        context.OrganizationClients.AddRange(client, otherClient);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        await command.UpdateAsync(organization.OwnerUserId, organization.Id, client.Id, new UpdateOrganizationClientDto
        {
            Name = " Новое имя ",
            Phone = " 123 ",
            PartyTypeCode = "company",
            Inn = " 1234567890 ",
            Kpp = "123456789",
            LegalAddress = " Новый адрес ",
            Comment = " комментарий ",
            StatusCode = "attention",
            ResponsibleUserId = organization.OwnerUserId,
            Notes = " заметка ",
            TagNames = ["Существующий, новый"]
        });

        var updated = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        var tags = await context.OrganizationClientTags.AsNoTracking()
            .Where(tag => tag.OrganizationId == organization.Id)
            .ToListAsync();
        var history = await context.OrganizationClientHistoryEvents.AsNoTracking().SingleAsync(item => item.OrganizationClientId == client.Id);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync(item => item.TargetId == client.Id.ToString("D"));

        Assert.Equal("Новое имя", updated.Name);
        Assert.Equal("123", updated.Phone);
        Assert.Equal("company", updated.PartyTypeCode);
        Assert.Equal("1234567890", updated.Inn);
        Assert.Equal("123456789", updated.Kpp);
        Assert.Equal("Новый адрес", updated.LegalAddress);
        Assert.Equal("attention", updated.StatusCode);
        Assert.Equal("комментарий\n\nзаметка", updated.Comment);
        Assert.Contains("Имя или название: Старое имя -> Новое имя", history.Description);
        Assert.Contains("Теги:", history.Description);
        Assert.Equal("clients.updated", audit.EventType);
        Assert.Equal(2, tags.Count);
        Assert.Equal(2, await context.OrganizationClientTagAssignments.CountAsync(assignment => assignment.OrganizationClientId == client.Id));
        Assert.Equal(1, await context.OrganizationClientTagAssignments.CountAsync(assignment => assignment.OrganizationClientId == otherClient.Id));
    }

    [Fact]
    public async Task StaleContextUpdateUsesReloadedValuesForChangeHistoryAndTagAssignments()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var oldTag = new OrganizationClientTag
        {
            OrganizationId = organization.Id,
            Name = "Старый",
            NormalizedName = "СТАРЫЙ"
        };
        var client = ClientsDatabaseFixture.CreateClient(organization, "Old");
        client.TagAssignments.Add(new OrganizationClientTagAssignment
        {
            OrganizationClient = client,
            OrganizationClientTag = oldTag
        });
        seedContext.OrganizationClients.Add(client);
        await seedContext.SaveChangesAsync();

        await using var contextA = fixture.CreateContext();
        var staleClient = await contextA.OrganizationClients
            .Include(item => item.TagAssignments)
            .ThenInclude(assignment => assignment.OrganizationClientTag)
            .SingleAsync(item => item.Id == client.Id);
        Assert.Equal("Old", staleClient.Name);
        Assert.Equal("Старый", Assert.Single(staleClient.TagAssignments).OrganizationClientTag.Name);
        using var servicesA = ClientCommandTestHarness.CreateServices(contextA);
        var commandA = servicesA.GetRequiredService<IOrganizationClientCommandService>();

        await using var contextB = fixture.CreateContext();
        using var servicesB = ClientCommandTestHarness.CreateServices(contextB);
        var commandB = servicesB.GetRequiredService<IOrganizationClientCommandService>();
        await commandB.UpdateAsync(organization.OwnerUserId, organization.Id, client.Id, new UpdateOrganizationClientDto
        {
            Name = "New",
            PartyTypeCode = "person",
            TagNames = ["Новый"]
        });

        await commandA.UpdateAsync(organization.OwnerUserId, organization.Id, client.Id, new UpdateOrganizationClientDto
        {
            Name = "Old",
            PartyTypeCode = "person",
            TagNames = ["Старый"]
        });

        await using var verificationContext = fixture.CreateContext();
        var updatedClient = await verificationContext.OrganizationClients.AsNoTracking()
            .SingleAsync(item => item.Id == client.Id);
        var assignments = await verificationContext.OrganizationClientTagAssignments.AsNoTracking()
            .Where(assignment => assignment.OrganizationClientId == client.Id)
            .Join(
                verificationContext.OrganizationClientTags.AsNoTracking(),
                assignment => assignment.OrganizationClientTagId,
                tag => tag.Id,
                (_, tag) => tag.Name)
            .ToListAsync();
        var histories = await verificationContext.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.OrganizationClientId == client.Id)
            .Select(item => item.Description!)
            .ToListAsync();

        Assert.Equal("Old", updatedClient.Name);
        Assert.Equal(new[] { "Старый" }, assignments);
        Assert.Single(assignments);
        Assert.Equal(2, histories.Count);
        Assert.Contains(histories, description =>
            description.Contains("Имя или название: Old -> New", StringComparison.Ordinal)
            && description.Contains("Теги: Старый -> Новый", StringComparison.Ordinal));
        Assert.Contains(histories, description =>
            description.Contains("Имя или название: New -> Old", StringComparison.Ordinal)
            && description.Contains("Теги: Новый -> Старый", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateRejectsClientArchivedBeforeItsRowLock()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент до архивации");
        seedContext.OrganizationClients.Add(client);
        await seedContext.SaveChangesAsync();

        IOrganizationClientCommandService? archiveCommand = null;
        var interceptor = new ArchiveClientBeforeUpdateLockInterceptor(
            () => archiveCommand!.ArchiveAsync(organization.OwnerUserId, organization.Id, client.Id));
        await using var updateContext = fixture.CreateContext(interceptor);
        using var updateServices = ClientCommandTestHarness.CreateServices(updateContext);
        var updateCommand = updateServices.GetRequiredService<IOrganizationClientCommandService>();

        await using var archiveContext = fixture.CreateContext();
        using var archiveServices = ClientCommandTestHarness.CreateServices(archiveContext);
        archiveCommand = archiveServices.GetRequiredService<IOrganizationClientCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => updateCommand.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            client.Id,
            new UpdateOrganizationClientDto { Name = "Не должно сохраниться", PartyTypeCode = "person" }));

        await using var verificationContext = fixture.CreateContext();
        var archivedClient = await verificationContext.OrganizationClients.AsNoTracking()
            .SingleAsync(item => item.Id == client.Id);
        Assert.Equal("Клиент не найден.", exception.Message);
        Assert.True(archivedClient.IsDeleted);
        Assert.NotNull(archivedClient.DeletionDate);
        Assert.Equal("Клиент до архивации", archivedClient.Name);
        Assert.Equal(new[] { "archived" }, await verificationContext.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.OrganizationClientId == client.Id)
            .Select(item => item.EventType)
            .ToArrayAsync());
        Assert.Equal(new[] { "clients.archived" }, await verificationContext.OrganizationAuditEvents.AsNoTracking()
            .Where(item => item.TargetId == client.Id.ToString("D"))
            .Select(item => item.EventType)
            .ToArrayAsync());
    }

    [Fact]
    public async Task NoOpUpdateWritesNoHistoryOrAudit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();
        var dto = new UpdateOrganizationClientDto { Name = client.Name, PartyTypeCode = "person", TagNames = [] };

        await command.UpdateAsync(organization.OwnerUserId, organization.Id, client.Id, dto);

        Assert.Empty(await context.OrganizationClientHistoryEvents.AsNoTracking().Where(item => item.OrganizationClientId == client.Id).ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().Where(item => item.TargetId == client.Id.ToString("D")).ToListAsync());
        Assert.Empty(await context.OrganizationClientTagAssignments.AsNoTracking().Where(item => item.OrganizationClientId == client.Id).ToListAsync());
    }

    [Fact]
    public async Task RemovingTagsUpdatesAssignmentsAndRecordsTheChange()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var tag = new OrganizationClientTag
        {
            OrganizationId = organization.Id,
            Name = "Убрать",
            NormalizedName = "УБРАТЬ"
        };
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        client.TagAssignments.Add(new OrganizationClientTagAssignment
        {
            OrganizationClient = client,
            OrganizationClientTag = tag
        });
        context.OrganizationClients.Add(client);
        context.OrganizationClientTags.Add(tag);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        await command.UpdateAsync(organization.OwnerUserId, organization.Id, client.Id, new UpdateOrganizationClientDto
        {
            Name = client.Name,
            PartyTypeCode = "person",
            TagNames = []
        });

        Assert.Empty(await context.OrganizationClientTagAssignments.AsNoTracking().Where(item => item.OrganizationClientId == client.Id).ToListAsync());
        var history = await context.OrganizationClientHistoryEvents.AsNoTracking().SingleAsync(item => item.OrganizationClientId == client.Id);
        Assert.Contains("Теги: Убрать -> не указано", history.Description);
        Assert.Equal("clients.updated", await context.OrganizationAuditEvents.AsNoTracking()
            .Where(item => item.TargetId == client.Id.ToString("D"))
            .Select(item => item.EventType)
            .SingleAsync());
    }

    [Fact]
    public async Task ArchivesWithoutUnlinkingVehicleThenRestoresClient()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        await command.DeleteAsync(organization.OwnerUserId, organization.Id, client.Id);

        var archived = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        Assert.True(archived.IsDeleted);
        Assert.Equal(client.Id, await context.Vehicles.AsNoTracking().Where(item => item.Id == vehicle.Id).Select(item => item.OrganizationClientId).SingleAsync());
        Assert.Equal("archived", await context.OrganizationClientHistoryEvents.AsNoTracking().Where(item => item.OrganizationClientId == client.Id).Select(item => item.EventType).SingleAsync());

        await command.RestoreAsync(organization.OwnerUserId, organization.Id, client.Id);

        var restored = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletionDate);
        Assert.Equal(new[] { "archived", "restored" }, await context.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.OrganizationClientId == client.Id)
            .OrderBy(item => item.CreatedAt)
            .Select(item => item.EventType)
            .ToArrayAsync());
    }

    [Fact]
    public async Task ManualHistoryTruncatesTextAndNormalizesDateToUtc()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();
        var suppliedDate = new DateTime(2025, 4, 3, 12, 30, 0, DateTimeKind.Unspecified);

        await command.AddHistoryEntryAsync(organization.OwnerUserId, organization.Id, client.Id, new string('т', 1001), suppliedDate);

        var history = await context.OrganizationClientHistoryEvents.AsNoTracking().SingleAsync(item => item.OrganizationClientId == client.Id);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync(item => item.TargetId == client.Id.ToString("D"));
        Assert.Equal(1000, history.Description?.Length);
        Assert.Equal(DateTimeKind.Utc, history.CreatedAt.Kind);
        Assert.Equal(DateTime.SpecifyKind(suppliedDate, DateTimeKind.Utc), history.CreatedAt);
        Assert.Equal("clients.history.created", audit.EventType);
    }

    [Fact]
    public async Task ManualHistoryWithoutDateUsesUtcNow()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();
        var before = DateTime.UtcNow;

        await command.AddHistoryEntryAsync(organization.OwnerUserId, organization.Id, client.Id, "Обычная запись", null);

        var history = await context.OrganizationClientHistoryEvents.AsNoTracking().SingleAsync(item => item.OrganizationClientId == client.Id);
        Assert.Equal(DateTimeKind.Utc, history.CreatedAt.Kind);
        Assert.InRange(history.CreatedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task RejectsManualHistoryForMissingArchivedAndForeignClients()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context, "Чужая организация");
        var archivedClient = ClientsDatabaseFixture.CreateClient(organization, "Архивный клиент");
        archivedClient.Delete(DateTime.UtcNow);
        var foreignClient = ClientsDatabaseFixture.CreateClient(foreignOrganization, "Чужой клиент");
        context.OrganizationClients.AddRange(archivedClient, foreignClient);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        foreach (var clientId in new[] { Guid.NewGuid(), archivedClient.Id, foreignClient.Id })
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.AddHistoryEntryAsync(
                organization.OwnerUserId, organization.Id, clientId, "Запись", null));
            Assert.Equal("Клиент не найден.", exception.Message);
        }
    }

    [Fact]
    public async Task RejectsArchivingMissingClientAndRestoringActiveClient()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Активный клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => command.ArchiveAsync(
            organization.OwnerUserId, organization.Id, Guid.NewGuid()));
        var active = await Assert.ThrowsAsync<InvalidOperationException>(() => command.RestoreAsync(
            organization.OwnerUserId, organization.Id, client.Id));

        Assert.Equal("Клиент не найден.", missing.Message);
        Assert.Equal("Клиент в архиве не найден.", active.Message);
    }

    [Fact]
    public async Task FailedAuditForeignKeyRollsBackClientHistoryAndAuditTogether()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();

        await Assert.ThrowsAsync<DbUpdateException>(() => command.CreateAsync(
            "missing-user",
            organization.Id,
            new CreateOrganizationClientDto { Name = "Не должно сохраниться", PartyTypeCode = "person" }));

        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.OrganizationClients.AnyAsync(client => client.Name == "Не должно сохраниться"));
        Assert.False(await verificationContext.OrganizationClientHistoryEvents.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await verificationContext.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task ConcurrentCreatesReuseOneOrganizationTag()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var barrier = new TwoOrganizationLockBarrier();
        await using var firstContext = fixture.CreateContext(barrier);
        await using var secondContext = fixture.CreateContext(barrier);
        using var firstServices = ClientCommandTestHarness.CreateServices(firstContext);
        using var secondServices = ClientCommandTestHarness.CreateServices(secondContext);
        var firstCommand = firstServices.GetRequiredService<IOrganizationClientCommandService>();
        var secondCommand = secondServices.GetRequiredService<IOrganizationClientCommandService>();

        var firstTask = firstCommand.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = "Первый клиент",
            PartyTypeCode = "person",
            TagNames = ["Общий тег"]
        });
        var secondTask = secondCommand.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = "Второй клиент",
            PartyTypeCode = "person",
            TagNames = ["Общий тег"]
        });
        await barrier.WaitForBothCommandsAsync();
        barrier.Release();
        await Task.WhenAll(firstTask, secondTask);

        await using var verificationContext = fixture.CreateContext();
        var tag = await verificationContext.OrganizationClientTags.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.NormalizedName == "ОБЩИЙ ТЕГ");
        Assert.Equal(2, await verificationContext.OrganizationClientTagAssignments.AsNoTracking()
            .CountAsync(item => item.OrganizationClientTagId == tag.Id));
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    public async Task FreeClientLimitIncludesArchivedClients(int existingCount, bool canCreate)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        context.OrganizationClients.AddRange(Enumerable.Range(0, existingCount).Select(index => new OrganizationClient
        {
            OrganizationId = organization.Id,
            Name = $"Архивный клиент {index}",
            IsDeleted = true
        }));
        await context.SaveChangesAsync();

        var subscriptions = CreateClientLimitSubscription(300);
        using var services = ClientCommandTestHarness.CreateServices(context, subscriptionService: subscriptions.Object);
        var command = services.GetRequiredService<IOrganizationClientCommandService>();
        if (canCreate)
        {
            await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
            {
                Name = "Клиент на границе",
                PartyTypeCode = "person"
            });
        }
        else
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.CreateAsync(
                organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
                {
                    Name = "Клиент сверх границы",
                    PartyTypeCode = "person"
                }));

            Assert.Contains("300 из 300", exception.Message, StringComparison.Ordinal);
        }

        context.ChangeTracker.Clear();
        Assert.Equal(existingCount + (canCreate ? 1 : 0), await context.OrganizationClients.CountAsync(client => client.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task ConcurrentClientCreatesCannotExceedOrganizationLimit()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var subscriptions = CreateClientLimitSubscription(1);
        using var firstServices = ClientCommandTestHarness.CreateServices(firstContext, subscriptionService: subscriptions.Object);
        using var secondServices = ClientCommandTestHarness.CreateServices(secondContext, subscriptionService: subscriptions.Object);
        var firstCommand = firstServices.GetRequiredService<IOrganizationClientCommandService>();
        var secondCommand = secondServices.GetRequiredService<IOrganizationClientCommandService>();

        var results = await Task.WhenAll(
            TryCreateAsync(firstCommand, organization.OwnerUserId, organization.Id, "Первый"),
            TryCreateAsync(secondCommand, organization.OwnerUserId, organization.Id, "Второй"));

        Assert.Single(results, created => created);
        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(1, await verificationContext.OrganizationClients.CountAsync(client => client.OrganizationId == organization.Id));
    }

    private static Mock<IOrganizationSubscriptionService> CreateClientLimitSubscription(int limit)
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto
            {
                Features = [new FeatureValueDto { Code = FeatureCodes.WorkspaceClientsLimit, IntValue = limit }]
            });

        return subscriptions;
    }

    private static async Task<bool> TryCreateAsync(
        IOrganizationClientCommandService command,
        string userId,
        Guid organizationId,
        string name)
    {
        try
        {
            await command.CreateAsync(userId, organizationId, new CreateOrganizationClientDto { Name = name, PartyTypeCode = "person" });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

internal static class ClientCommandTestHarness
{
    internal static ServiceProvider CreateServices(
        OpenCRMContext context,
        Action<Mock<IOrganizationAuditService>>? configureAudit = null,
        IOrganizationSubscriptionService? subscriptionService = null)
    {
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto());

        var audit = new Mock<IOrganizationAuditService>();
        audit.Setup(service => service.AddAsync(It.IsAny<OrganizationAuditEventCreateDto>(), It.IsAny<bool>()))
            .Callback<OrganizationAuditEventCreateDto, bool>((dto, saveChanges) =>
            {
                if (saveChanges)
                {
                    throw new InvalidOperationException("Аудит команды клиента должен добавляться без отдельного сохранения.");
                }

                context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
                {
                    OrganizationId = dto.OrganizationId,
                    ActorUserId = dto.ActorUserId,
                    EventType = dto.EventType,
                    Title = dto.Title,
                    Description = dto.Description,
                    TargetType = dto.TargetType,
                    TargetId = dto.TargetId
                });
            })
            .Returns(Task.CompletedTask);
        configureAudit?.Invoke(audit);

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(subscriptionService ?? subscription.Object);
        services.AddSingleton(audit.Object);
        services.AddOpenCRMClients();

        return services.BuildServiceProvider();
    }
}

internal sealed class TwoOrganizationLockBarrier : DbCommandInterceptor
{
    private readonly TaskCompletionSource _bothCommands = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _commandCount;

    internal Task WaitForBothCommandsAsync() => this._bothCommands.Task.WaitAsync(TimeSpan.FromSeconds(15));

    internal void Release() => this._release.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
        {
            if (Interlocked.Increment(ref this._commandCount) == 2)
            {
                this._bothCommands.TrySetResult();
            }

            await this._release.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}

internal sealed class ArchiveClientBeforeUpdateLockInterceptor(Func<Task> archiveClient) : DbCommandInterceptor
{
    private int _hasArchived;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("\"OrganizationClients\"", StringComparison.Ordinal)
            && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
            && Interlocked.Exchange(ref this._hasArchived, 1) == 0)
        {
            await archiveClient();
        }

        return result;
    }
}
