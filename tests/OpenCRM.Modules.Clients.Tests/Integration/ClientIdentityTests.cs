using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientIdentityTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task PersonNamePartsAreNormalizedAndReturnedByDetailsAndPagedQueries()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();
        var clientId = await commands.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = string.Empty,
            PartyTypeCode = "person",
            LastName = " Иванов ",
            FirstName = " Иван ",
            MiddleName = " Иванович "
        });

        var client = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == clientId);
        Assert.Equal("Иванов Иван Иванович", client.Name);
        Assert.Equal("Иванов", client.LastName);
        Assert.Equal("Иван", client.FirstName);
        Assert.Equal("Иванович", client.MiddleName);

        var queries = services.GetRequiredService<IOrganizationClientQueryService>();
        var details = await queries.GetByIdAsync(organization.OwnerUserId, organization.Id, clientId);
        var page = await queries.GetPageAsync(organization.OwnerUserId, organization.Id, null, null, 1, 10);
        var pageItem = Assert.Single(page.Items);
        AssertIdentity(details!, "Иванов", "Иван", "Иванович", null);
        AssertIdentity(pageItem, "Иванов", "Иван", "Иванович", null);

        var detailedCard = await services.GetRequiredService<IOrganizationClientDetailsQueryService>()
            .GetDetailsAsync(organization.OwnerUserId, organization.Id, clientId);
        Assert.NotNull(detailedCard);
        AssertIdentity(detailedCard.Client, "Иванов", "Иван", "Иванович", null);
    }

    [Fact]
    public async Task LegacyUpdatePreservesNamePartsOnlyWhenDisplayNameIsUnchangedAndSwitchingClearsThem()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();
        var clientId = await commands.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = string.Empty,
            PartyTypeCode = "person",
            LastName = "Петров",
            FirstName = "Пётр",
            MiddleName = "Сергеевич"
        });
        var name = await context.OrganizationClients.Where(item => item.Id == clientId).Select(item => item.Name).SingleAsync();

        await commands.UpdateAsync(organization.OwnerUserId, organization.Id, clientId, new UpdateOrganizationClientDto
        {
            Name = name,
            PartyTypeCode = "person"
        });

        var preserved = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == clientId);
        Assert.Equal("Петров", preserved.LastName);
        Assert.Equal("Пётр", preserved.FirstName);
        Assert.Equal("Сергеевич", preserved.MiddleName);

        await commands.UpdateAsync(organization.OwnerUserId, organization.Id, clientId, new UpdateOrganizationClientDto
        {
            Name = "Пётр Петров",
            PartyTypeCode = "person"
        });
        var renamed = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == clientId);
        Assert.Null(renamed.LastName);
        Assert.Null(renamed.FirstName);
        Assert.Null(renamed.MiddleName);

        await commands.UpdateAsync(organization.OwnerUserId, organization.Id, clientId, new UpdateOrganizationClientDto
        {
            Name = "ООО Ромашка",
            PartyTypeCode = "company",
            CompanyTypeCode = "legal_entity",
            Inn = "0012345678",
            Kpp = "1234AB567"
        });
        var company = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == clientId);
        Assert.Null(company.LastName);
        Assert.Null(company.FirstName);
        Assert.Null(company.MiddleName);
        Assert.Equal("legal_entity", company.CompanyTypeCode);

        await commands.UpdateAsync(organization.OwnerUserId, organization.Id, clientId, new UpdateOrganizationClientDto
        {
            Name = "Новый клиент",
            PartyTypeCode = "person"
        });
        var person = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == clientId);
        Assert.Null(person.CompanyTypeCode);
        Assert.Null(person.Inn);
        Assert.Null(person.Kpp);
    }

    [Fact]
    public async Task NamePartLimitAndCombinedNameLimitAreValidatedWithoutMutatingClient()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();
        var clientId = await commands.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = string.Empty,
            PartyTypeCode = "person",
            LastName = new string('Ф', 60),
            FirstName = new string('И', 60),
            MiddleName = new string('О', 38)
        });
        var originalName = await context.OrganizationClients.Where(item => item.Id == clientId).Select(item => item.Name).SingleAsync();
        Assert.Equal(160, originalName.Length);

        var invalidUpdates = new[]
        {
            new UpdateOrganizationClientDto
            {
                Name = originalName,
                PartyTypeCode = "person",
                LastName = new string('Ф', 60),
                FirstName = new string('И', 60),
                MiddleName = new string('О', 39)
            },
            new UpdateOrganizationClientDto
            {
                Name = originalName,
                PartyTypeCode = "person",
                LastName = new string('Ф', 61),
                FirstName = "И"
            },
            new UpdateOrganizationClientDto
            {
                Name = originalName,
                PartyTypeCode = "person",
                LastName = "Иванов"
            }
        };

        foreach (var update in invalidUpdates)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => commands.UpdateAsync(
                organization.OwnerUserId, organization.Id, clientId, update));
        }

        var unchanged = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == clientId);
        Assert.Equal(originalName, unchanged.Name);
        Assert.Equal(new string('Ф', 60), unchanged.LastName);
        Assert.Equal(new string('И', 60), unchanged.FirstName);
        Assert.Equal(new string('О', 38), unchanged.MiddleName);
    }

    [Fact]
    public async Task CompanyTypeValidatesInnAndKppAndKeepsLegacyAccountingLimits()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();

        var legalEntityId = await commands.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = "ООО Ромашка",
            PartyTypeCode = "company",
            CompanyTypeCode = "legal_entity",
            Inn = " 00 123 456 78 ",
            Kpp = " 1234ab567 "
        });
        var legalEntity = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == legalEntityId);
        Assert.Equal("0012345678", legalEntity.Inn);
        Assert.Equal("1234AB567", legalEntity.Kpp);

        var entrepreneurId = await commands.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = "ИП Иванов",
            PartyTypeCode = "company",
            CompanyTypeCode = "entrepreneur",
            Inn = " 001 234 567 890 ",
            Kpp = "   "
        });
        var entrepreneur = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == entrepreneurId);
        Assert.Equal("001234567890", entrepreneur.Inn);
        Assert.Null(entrepreneur.Kpp);

        var legacyId = await commands.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = "Старая компания",
            PartyTypeCode = "company",
            Inn = new string('1', 20),
            Kpp = new string('2', 20)
        });
        var legacy = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == legacyId);
        Assert.Null(legacy.CompanyTypeCode);
        Assert.Equal(20, legacy.Inn!.Length);
        Assert.Equal(20, legacy.Kpp!.Length);

        var invalidRequests = new[]
        {
            new CreateOrganizationClientDto { Name = "Неверный ИНН", PartyTypeCode = "company", CompanyTypeCode = "legal_entity", Inn = "123456789", Kpp = "123456789" },
            new CreateOrganizationClientDto { Name = "Неверный КПП", PartyTypeCode = "company", CompanyTypeCode = "legal_entity", Inn = "1234567890", Kpp = "1234АБ567" },
            new CreateOrganizationClientDto { Name = "ИП с КПП", PartyTypeCode = "company", CompanyTypeCode = "entrepreneur", Inn = "123456789012", Kpp = "123456789" },
            new CreateOrganizationClientDto { Name = "Неверный вид", PartyTypeCode = "company", CompanyTypeCode = "unknown", Inn = "1234567890" }
        };

        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => commands.CreateAsync(
                organization.OwnerUserId, organization.Id, request));
        }

        var clients = await context.OrganizationClients.AsNoTracking().Where(item => item.OrganizationId == organization.Id).ToListAsync();
        Assert.Equal(3, clients.Count);

        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            legalEntityId,
            new UpdateOrganizationClientDto
            {
                Name = "ООО Ромашка",
                PartyTypeCode = "company",
                Inn = "12345678901",
                Kpp = "1234AB567"
            }));
        var legacyUpdate = new UpdateOrganizationClientDto
        {
            Name = legalEntity.Name,
            PartyTypeCode = "company",
            Inn = legalEntity.Inn,
            Kpp = legalEntity.Kpp
        };
        await commands.UpdateAsync(organization.OwnerUserId, organization.Id, legalEntityId, legacyUpdate);
        Assert.Equal("legal_entity", await context.OrganizationClients.AsNoTracking()
            .Where(item => item.Id == legalEntityId)
            .Select(item => item.CompanyTypeCode)
            .SingleAsync());
    }

    [Fact]
    public async Task FullUpdateAcceptsMaximumCommentAndRejectsOversizedCommentWithoutMutation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();
        var clientId = await commands.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
        {
            Name = "Клиент",
            PartyTypeCode = "person"
        });

        var firstComment = new string('А', 4000);
        await commands.UpdateAsync(organization.OwnerUserId, organization.Id, clientId, new UpdateOrganizationClientDto
        {
            Name = "Клиент",
            PartyTypeCode = "person",
            Comment = firstComment
        });

        var secondComment = new string('Б', 4000);
        await commands.UpdateAsync(organization.OwnerUserId, organization.Id, clientId, new UpdateOrganizationClientDto
        {
            Name = "Клиент",
            PartyTypeCode = "person",
            Comment = secondComment
        });
        Assert.Equal(secondComment, await context.OrganizationClients.AsNoTracking()
            .Where(item => item.Id == clientId)
            .Select(item => item.Comment)
            .SingleAsync());

        await commands.UpdateAsync(organization.OwnerUserId, organization.Id, clientId, new UpdateOrganizationClientDto
        {
            Name = "Клиент",
            PartyTypeCode = "person",
            Comment = null
        });

        var oversizedComment = new string('В', 4001);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            clientId,
            new UpdateOrganizationClientDto
            {
                Name = "Клиент",
                PartyTypeCode = "person",
                Comment = oversizedComment
            }));

        var client = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == clientId);
        Assert.Null(client.Comment);
    }

    private static void AssertIdentity(
        OpenCRM.Modules.Clients.Contracts.Dtos.OrganizationClientDto client,
        string? lastName,
        string? firstName,
        string? middleName,
        string? companyTypeCode)
    {
        Assert.Equal(lastName, client.LastName);
        Assert.Equal(firstName, client.FirstName);
        Assert.Equal(middleName, client.MiddleName);
        Assert.Equal(companyTypeCode, client.CompanyTypeCode);
    }
}
