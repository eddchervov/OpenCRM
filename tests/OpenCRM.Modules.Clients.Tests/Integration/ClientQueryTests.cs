using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientQueryTests(ClientsDatabaseFixture fixture)
{
    private const string UserId = "client-query-test";

    [Theory]
    [InlineData("+79990001031", "+7 (999) 000-10-31")]
    [InlineData("8 (999) 000-10-31", "+79990001031")]
    [InlineData("9990001031", "89990001031")]
    public async Task PageSearchMatchesEquivalentPhoneFormats(string storedPhone, string search)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Поиск телефона");
        client.Phone = storedPhone;
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var result = await query.GetPageAsync(UserId, organization.Id, search, null, 1, 10);

        Assert.Equal(client.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task CancelledOrderHasNoClientDebtAndKeepsPaymentUntilItIsCancelled()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Отменённая запись");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        var maintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Cancelled, DateTime.UtcNow);
        maintenance.BuyerClientId = client.Id;
        maintenance.DiscountAmount = 100m;
        maintenance.PaidAmount = 200m;
        maintenance.Details.Add(new MaintenanceDetail { Name = "Работа", Price = 1000m });
        var payment = new MaintenancePayment { Id = Guid.NewGuid(), Maintenance = maintenance, Amount = 200m };
        context.Maintenance.Add(maintenance);
        context.MaintenancePayments.Add(payment);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();
        var detailsQuery = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var cancelled = Assert.Single((await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10)).Items);
        var details = await detailsQuery.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.Equal(0m, cancelled.TotalDebtAmount);
        Assert.Equal(200m, cancelled.TotalPaidAmount);
        Assert.Equal(200m, details!.TotalPaidAmount);
        var cancelledHistory = Assert.Single(details.RecentMaintenances);
        Assert.Equal(0m, cancelledHistory.NetAmount);
        Assert.Equal(200m, cancelledHistory.PaidAmount);
        Assert.Equal(1000m, cancelledHistory.GrossAmount);

        payment.CancelledAt = DateTime.UtcNow;
        maintenance.PaidAmount = 0m;
        await context.SaveChangesAsync();
        var withoutPayment = Assert.Single((await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10)).Items);

        Assert.Equal(0m, withoutPayment.TotalDebtAmount);
        Assert.Equal(0m, withoutPayment.TotalPaidAmount);

        maintenance.Status = MaintenanceStatus.Planned;
        await context.SaveChangesAsync();
        var reopened = Assert.Single((await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10)).Items);

        Assert.Equal(900m, reopened.TotalDebtAmount);
        Assert.Equal(0m, reopened.TotalPaidAmount);
    }

    [Fact]
    public async Task FiltersPageByPartyTypeAndReturnsFinanceTotalsOnlyWithPermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var company = ClientsDatabaseFixture.CreateClient(organization, "Компания");
        company.PartyTypeCode = "company";
        var person = ClientsDatabaseFixture.CreateClient(organization, "Частный клиент");
        person.PartyTypeCode = "person";
        context.OrganizationClients.AddRange(company, person);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, company);
        var maintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-1));
        maintenance.BuyerClientId = company.Id;
        maintenance.DiscountAmount = 100;
        maintenance.PaidAmount = 390;
        maintenance.Details.Add(new MaintenanceDetail { Name = "Полировка", Price = 1_000 });
        context.Maintenance.Add(maintenance);
        context.MaintenancePayments.AddRange(
            new MaintenancePayment { Id = Guid.NewGuid(), Maintenance = maintenance, Amount = 350m },
            new MaintenancePayment { Id = Guid.NewGuid(), Maintenance = maintenance, Amount = 40m },
            new MaintenancePayment
            {
                Id = Guid.NewGuid(),
                Maintenance = maintenance,
                Amount = 100m,
                CancelledAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();
        using var permittedServices = fixture.CreateServices(context);
        var permittedQuery = permittedServices.GetRequiredService<IOrganizationClientQueryService>();

        var companies = await permittedQuery.GetPageAsync(
            UserId, organization.Id, null, null, 1, 10, partyTypeCode: " COMPANY ");
        var companyRow = Assert.Single(companies.Items);

        Assert.Equal(company.Id, companyRow.Id);
        Assert.Equal(390m, companyRow.TotalPaidAmount);
        Assert.Equal(510m, companyRow.TotalDebtAmount);
        var details = await permittedServices.GetRequiredService<IOrganizationClientDetailsQueryService>()
            .GetDetailsAsync(UserId, organization.Id, company.Id);
        Assert.Equal(details!.TotalPaidAmount, companyRow.TotalPaidAmount);
        Assert.Equal(2, (await permittedQuery.GetPageAsync(
            UserId, organization.Id, null, null, 1, 10, partyTypeCode: "unknown")).Items.Count);

        using var restrictedServices = fixture.CreateServices(context, deniedPermissionCodes: new HashSet<string>
        {
            OrganizationPermissionCodes.FinanceView,
            OrganizationPermissionCodes.FinanceManage
        });
        var restrictedQuery = restrictedServices.GetRequiredService<IOrganizationClientQueryService>();
        var restrictedPage = await restrictedQuery.GetPageAsync(UserId, organization.Id, null, null, 1, 10);
        var restrictedCompany = Assert.Single(restrictedPage.Items, item => item.Id == company.Id);

        Assert.Null(restrictedCompany.TotalPaidAmount);
        Assert.Null(restrictedCompany.TotalDebtAmount);
    }

    [Fact]
    public async Task ActiveClientCountIsScopedToOrganizationAndExcludesArchivedClients()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context, "Соседнее пространство");
        var activeClients = Enumerable.Range(0, 3)
            .Select(index => ClientsDatabaseFixture.CreateClient(organization, $"Активный {index}"))
            .ToArray();
        var archivedClient = ClientsDatabaseFixture.CreateClient(organization, "В архиве");
        archivedClient.Delete(DateTime.UtcNow);
        var foreignClient = ClientsDatabaseFixture.CreateClient(otherOrganization, "В другом пространстве");
        context.OrganizationClients.AddRange(activeClients.Append(archivedClient).Append(foreignClient));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var counts = services.GetRequiredService<IOrganizationClientCountQueryService>();

        var result = await counts.GetActiveCountAsync(organization.Id);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task ReadsOnlyActiveClientsFromTheRequestedOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context, "Другое пространство");
        var client = ClientsDatabaseFixture.CreateClient(organization, "Активный клиент");
        var archivedClient = ClientsDatabaseFixture.CreateClient(organization, "Архивный клиент");
        archivedClient.Delete(DateTime.UtcNow);
        var foreignClient = ClientsDatabaseFixture.CreateClient(otherOrganization, "Чужой клиент");
        context.OrganizationClients.AddRange(client, archivedClient, foreignClient);
        await context.SaveChangesAsync();

        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var loaded = await query.GetByIdAsync(UserId, organization.Id, client.Id);
        var archived = await query.GetByIdAsync(UserId, organization.Id, archivedClient.Id);
        var foreign = await query.GetByIdAsync(UserId, organization.Id, foreignClient.Id);
        var activePage = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10);
        var archivedPage = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, isArchived: true);

        Assert.Equal(client.Id, loaded?.Id);
        Assert.Null(archived);
        Assert.Null(foreign);
        Assert.Equal(client.Id, Assert.Single(activePage.Items).Id);
        Assert.Equal(archivedClient.Id, Assert.Single(archivedPage.Items).Id);
    }

    [Fact]
    public async Task ClientAggregatesRespectBuyerLinksAndSoftDeletes()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент с заказами");
        var otherClient = ClientsDatabaseFixture.CreateClient(organization, "Другой клиент");
        context.OrganizationClients.AddRange(client, otherClient);
        await context.SaveChangesAsync();
        var assignedVehicle = await fixture.AddVehicleAsync(context, organization, client, "Связанная техника");
        var unassignedVehicle = await fixture.AddVehicleAsync(context, organization, name: "Техника с покупателем");
        var deletedVehicle = await fixture.AddVehicleAsync(context, organization, client, "Удалённая техника", isDeleted: true);
        var lastServiceDate = DateTime.UtcNow.AddDays(-1);
        var ownerBuyerMaintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, assignedVehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-20));
        var explicitBuyer = ClientsDatabaseFixture.CreateMaintenance(
            organization, unassignedVehicle, MaintenanceStatus.Completed, lastServiceDate);
        explicitBuyer.BuyerClientId = client.Id;
        var differentBuyer = ClientsDatabaseFixture.CreateMaintenance(
            organization, assignedVehicle, MaintenanceStatus.Completed, DateTime.UtcNow);
        differentBuyer.BuyerClientId = otherClient.Id;
        var explicitNoBuyer = ClientsDatabaseFixture.CreateMaintenance(
            organization, assignedVehicle, MaintenanceStatus.Completed, DateTime.UtcNow);
        explicitNoBuyer.BuyerClientId = null;
        var deletedMaintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, assignedVehicle, MaintenanceStatus.Completed, DateTime.UtcNow);
        deletedMaintenance.IsDeleted = true;
        var deletedVehicleMaintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, deletedVehicle, MaintenanceStatus.Completed, DateTime.UtcNow);
        context.Maintenance.AddRange(
            ownerBuyerMaintenance,
            explicitBuyer,
            differentBuyer,
            explicitNoBuyer,
            deletedMaintenance,
            deletedVehicleMaintenance);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var clientDto = await query.GetByIdAsync(UserId, organization.Id, client.Id);
        var options = await query.GetOptionsAsync(UserId, organization.Id);

        Assert.NotNull(clientDto);
        Assert.Equal(1, clientDto.VehiclesCount);
        Assert.Equal(2, clientDto.MaintenanceCount);
        Assert.Equal(lastServiceDate.Date, clientDto.LastMaintenanceDate?.Date);
        var vehicleDto = Assert.Single(clientDto.Vehicles);
        Assert.Equal(assignedVehicle.Id, vehicleDto.Id);
        Assert.Equal(3, vehicleDto.MaintenanceCount);
        Assert.Equal(explicitNoBuyer.Date.Date, vehicleDto.LastMaintenanceDate?.Date);
        Assert.Equal(2, options.Count);
        Assert.Equal(1, Assert.Single(options, option => option.Id == client.Id).VehiclesCount);

        var page = await query.GetPageAsync(UserId, organization.Id, null, "maintenance_desc", 1, 10);
        var pageClient = Assert.Single(page.Items, item => item.Id == client.Id);
        Assert.Equal(clientDto.MaintenanceCount, pageClient.MaintenanceCount);
        Assert.Equal(clientDto.LastMaintenanceDate, pageClient.LastMaintenanceDate);
        Assert.Equal(3, page.TotalMaintenanceCount);
    }

    [Fact]
    public async Task BuyerIdDeterminesClientAggregatesAndHistory()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var owner = ClientsDatabaseFixture.CreateClient(organization, "Владелец");
        var buyer = ClientsDatabaseFixture.CreateClient(organization, "Покупатель");
        context.OrganizationClients.AddRange(owner, buyer);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, owner);
        var date = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var maintenance = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Completed, date);
        maintenance.BuyerClientId = buyer.Id;
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var ownerCard = await query.GetByIdAsync(UserId, organization.Id, owner.Id);
        var buyerCard = await query.GetByIdAsync(UserId, organization.Id, buyer.Id);
        var page = await query.GetPageAsync(UserId, organization.Id, null, "maintenance_desc", 1, 10);
        var ownerRow = Assert.Single(page.Items, item => item.Id == owner.Id);
        var buyerRow = Assert.Single(page.Items, item => item.Id == buyer.Id);

        Assert.Equal(0, ownerCard!.MaintenanceCount);
        Assert.Null(ownerCard.LastMaintenanceDate);
        Assert.Equal(1, buyerCard!.MaintenanceCount);
        Assert.Equal(date, buyerCard.LastMaintenanceDate);
        Assert.Equal(0, ownerRow.MaintenanceCount);
        Assert.Equal(1, buyerRow.MaintenanceCount);
        Assert.Equal(date, buyerRow.LastMaintenanceDate);
        Assert.Equal(1, page.TotalMaintenanceCount);
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();
        var ownerDetails = await details.GetDetailsAsync(UserId, organization.Id, owner.Id);
        var buyerDetails = await details.GetDetailsAsync(UserId, organization.Id, buyer.Id);
        Assert.Empty(ownerDetails!.RecentMaintenances);
        Assert.Equal(maintenance.Id, Assert.Single(buyerDetails!.RecentMaintenances).Id);
    }

    [Fact]
    public async Task MaintenanceWithoutBuyerIsNotAttributedToVehicleOwner()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var owner = ClientsDatabaseFixture.CreateClient(organization, "Владелец");
        context.OrganizationClients.Add(owner);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, owner);
        var maintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        maintenance.BuyerClientId = null;
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var client = await query.GetByIdAsync(UserId, organization.Id, owner.Id);
        var page = await query.GetPageAsync(UserId, organization.Id, null, "maintenance_desc", 1, 10);
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();
        var clientDetails = await details.GetDetailsAsync(UserId, organization.Id, owner.Id);

        Assert.NotNull(client);
        Assert.Equal(1, client.VehiclesCount);
        Assert.Equal(0, client.MaintenanceCount);
        Assert.Null(client.LastMaintenanceDate);
        var pageClient = Assert.Single(page.Items);
        Assert.Equal(1, pageClient.VehiclesCount);
        Assert.Equal(0, pageClient.MaintenanceCount);
        Assert.Null(pageClient.LastMaintenanceDate);
        Assert.Empty(clientDetails!.RecentMaintenances);
        Assert.DoesNotContain(clientDetails.HistoryItems, item => item.RelatedEntityId == maintenance.Id);
    }

    [Theory]
    [InlineData("maintenance_desc")]
    [InlineData("recent_desc")]
    public async Task BuyerAttributionIsAppliedBeforeSortingAndPagination(string sort)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var clients = Enumerable.Range(0, 12)
            .Select(index => ClientsDatabaseFixture.CreateClient(organization, $"Клиент {index:D2}"))
            .ToArray();
        context.OrganizationClients.AddRange(clients);
        await context.SaveChangesAsync();
        var initialDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Vehicle? ownerVehicle = null;

        for (var index = 0; index < clients.Length; index++)
        {
            var vehicle = await fixture.AddVehicleAsync(context, organization, clients[index]);
            context.Maintenance.Add(ClientsDatabaseFixture.CreateMaintenance(
                organization, vehicle, MaintenanceStatus.Completed, initialDate));

            if (index == 10)
            {
                ownerVehicle = vehicle;
            }
        }

        var latestDate = initialDate.AddMonths(2);
        var firstBuyerMaintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, ownerVehicle!, MaintenanceStatus.Completed, latestDate.AddDays(-1));
        var secondBuyerMaintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, ownerVehicle!, MaintenanceStatus.Completed, latestDate);
        firstBuyerMaintenance.BuyerClientId = clients[11].Id;
        secondBuyerMaintenance.BuyerClientId = clients[11].Id;
        context.Maintenance.AddRange(firstBuyerMaintenance, secondBuyerMaintenance);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var firstPage = await query.GetPageAsync(UserId, organization.Id, null, sort, 1, 10);
        var secondPage = await query.GetPageAsync(UserId, organization.Id, null, sort, 2, 10);

        Assert.Equal(12, firstPage.TotalCount);
        Assert.Equal(10, firstPage.Items.Count);
        Assert.Equal(clients[11].Id, firstPage.Items[0].Id);
        Assert.Equal(3, firstPage.Items[0].MaintenanceCount);
        Assert.Equal(latestDate, firstPage.Items[0].LastMaintenanceDate);
        Assert.DoesNotContain(secondPage.Items, client => client.Id == clients[11].Id);
        var ownerRow = Assert.Single(secondPage.Items, client => client.Id == clients[10].Id);
        Assert.Equal(1, ownerRow.MaintenanceCount);
        Assert.Equal(initialDate, ownerRow.LastMaintenanceDate);
        Assert.Equal(14, firstPage.TotalMaintenanceCount);
    }

    [Theory]
    [InlineData("name", "Искать по имени")]
    [InlineData("phone", "+7 900 111-22-33")]
    [InlineData("comment", "примечание для поиска")]
    public async Task SearchesEachClientTextField(string field, string value)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, field == "name" ? value : "Найденный клиент");
        switch (field)
        {
            case "phone":
                client.Phone = value;
                break;
            case "comment":
                client.Comment = value;
                break;
        }

        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var result = await query.GetPageAsync(UserId, organization.Id, value, null, 1, 10);
        var listResult = await query.GetAsync(UserId, organization.Id, value);

        Assert.Equal(client.Id, Assert.Single(result.Items).Id);
        Assert.Equal(client.Id, Assert.Single(listResult).Id);
    }

    [Fact]
    public async Task FiltersByEveryStatusResponsibleUserAndTag()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var clients = OrganizationClientStatusCodes.All
            .Select((status, index) => new OrganizationClient
            {
                Organization = organization,
                Name = $"Клиент {index}",
                StatusCode = status,
                ResponsibleUserId = index switch
                {
                    0 => null,
                    1 => "nameless-user",
                    2 => "deleted-user",
                    _ => "responsible-user"
                }
            })
            .ToArray();
        var tag = new OrganizationClientTag
        {
            Organization = organization,
            Name = "Постоянный",
            NormalizedName = "постоянный"
        };
        clients[0].TagAssignments.Add(new OrganizationClientTagAssignment
        {
            OrganizationClient = clients[0],
            OrganizationClientTag = tag
        });
        context.Users.Add(new ApplicationUser
        {
            Id = "responsible-user",
            UserName ="responsible.user",
            Email = "responsible@example.test"
        });
        context.Users.Add(new ApplicationUser { Id = "nameless-user", IsDeleted = false });
        context.Users.Add(new ApplicationUser
        {
            Id = "deleted-user",
            UserName ="deleted.user",
            Email = "deleted@example.test",
            IsDeleted = true
        });
        context.OrganizationClients.AddRange(clients);
        context.OrganizationClientTags.Add(tag);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        foreach (var (status, client) in OrganizationClientStatusCodes.All.Zip(clients))
        {
            var result = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, statusCode: status);
            Assert.Equal(client.Id, Assert.Single(result.Items).Id);
        }

        var unassigned = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, responsibleUserId: "__none");
        var assigned = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, responsibleUserId: "responsible-user");
        var nameless = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, responsibleUserId: "nameless-user");
        var tagged = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, tagId: tag.Id);

        Assert.Equal(clients[0].Id, Assert.Single(unassigned.Items).Id);
        Assert.Equal(2, assigned.TotalCount);
        Assert.Equal("nameless-user", Assert.Single(nameless.Items).ResponsibleDisplayName);
        var deleted = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, responsibleUserId: "deleted-user");
        Assert.Equal("deleted@example.test (удаленный пользователь)", Assert.Single(deleted.Items).ResponsibleDisplayName);
        Assert.Equal(clients[0].Id, Assert.Single(tagged.Items).Id);
    }

    [Fact]
    public async Task ProvidesAllSegmentsAndTreatsUnknownSegmentAsAll()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var lead = ClientsDatabaseFixture.CreateClient(organization, "Лид");
        lead.StatusCode = OrganizationClientStatusCodes.Lead;
        var attention = ClientsDatabaseFixture.CreateClient(organization, "Внимание", "worker");
        attention.StatusCode = OrganizationClientStatusCodes.Attention;
        var noRecentService = ClientsDatabaseFixture.CreateClient(organization, "Старое обслуживание", "worker");
        var noVehicles = ClientsDatabaseFixture.CreateClient(organization, "Без техники");
        var oldVehicle = await fixture.AddVehicleAsync(context, organization, noRecentService, "Старый автомобиль");
        var recentVehicle = await fixture.AddVehicleAsync(context, organization, attention, "Автомобиль с дефектом");
        context.Maintenance.Add(ClientsDatabaseFixture.CreateMaintenance(
            organization, oldVehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-300)));
        context.VehicleDefects.Add(new VehicleDefect
        {
            Organization = organization,
            Vehicle = recentVehicle,
            UserId = organization.OwnerUserId,
            Title = "Повреждение",
            StatusCode = VehicleDefectStatusCodes.New
        });
        context.OrganizationClients.AddRange(lead, noVehicles);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var segments = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10);
        var leads = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, segmentCode: "leads");
        var normalizedLeads = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, segmentCode: " LEADS ");
        var attentionClients = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, segmentCode: "attention");
        var unassigned = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, segmentCode: "no_responsible");
        var withoutVehicles = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, segmentCode: "no_vehicles");
        var withoutRecentService = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, segmentCode: "no_recent_service");
        var unknown = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 10, segmentCode: "unknown");

        Assert.Equal(6, segments.Segments.Count);
        Assert.Equal(lead.Id, Assert.Single(leads.Items).Id);
        Assert.Equal(lead.Id, Assert.Single(normalizedLeads.Items).Id);
        Assert.Contains(attentionClients.Items, item => item.Id == attention.Id);
        Assert.Contains(unassigned.Items, item => item.Id == lead.Id);
        Assert.Contains(unassigned.Items, item => item.Id == noVehicles.Id);
        Assert.Contains(withoutVehicles.Items, item => item.Id == noVehicles.Id);
        Assert.Contains(withoutRecentService.Items, item => item.Id == noRecentService.Id);
        Assert.Equal(segments.TotalCount, unknown.TotalCount);
    }

    [Fact]
    public async Task AppliesAllSortsAndNormalizesPageOptions()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var alpha = ClientsDatabaseFixture.CreateClient(organization, "Альфа");
        var beta = ClientsDatabaseFixture.CreateClient(organization, "Бета");
        var gamma = ClientsDatabaseFixture.CreateClient(organization, "Гамма");
        var betaVehicle1 = await fixture.AddVehicleAsync(context, organization, beta, "Автомобиль 1");
        var betaVehicle2 = await fixture.AddVehicleAsync(context, organization, beta, "Автомобиль 2");
        var gammaVehicle = await fixture.AddVehicleAsync(context, organization, gamma, "Автомобиль 3");
        context.Maintenance.AddRange(
            ClientsDatabaseFixture.CreateMaintenance(organization, betaVehicle1, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-1)),
            ClientsDatabaseFixture.CreateMaintenance(organization, betaVehicle2, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-2)),
            ClientsDatabaseFixture.CreateMaintenance(organization, gammaVehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-10)));
        context.OrganizationClients.Add(alpha);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var nameAsc = await query.GetPageAsync(UserId, organization.Id, null, "name_asc", 1, 10);
        var nameDesc = await query.GetPageAsync(UserId, organization.Id, null, "name_desc", 1, 10);
        var vehicles = await query.GetPageAsync(UserId, organization.Id, null, "vehicles_desc", 1, 10);
        var maintenance = await query.GetPageAsync(UserId, organization.Id, null, "maintenance_desc", 1, 10);
        var recent = await query.GetPageAsync(UserId, organization.Id, null, "recent_desc", 1, 10);
        var defaultPageSize = await query.GetPageAsync(UserId, organization.Id, null, null, 0, 0);
        var page25 = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 25);
        var page50 = await query.GetPageAsync(UserId, organization.Id, null, null, 1, 50);
        var outOfRange = await query.GetPageAsync(UserId, organization.Id, null, null, 8, 10);

        Assert.Equal(new[] { "Альфа", "Бета", "Гамма" }, nameAsc.Items.Select(item => item.Name));
        Assert.Equal(new[] { "Гамма", "Бета", "Альфа" }, nameDesc.Items.Select(item => item.Name));
        Assert.Equal(beta.Id, vehicles.Items[0].Id);
        Assert.Equal(beta.Id, maintenance.Items[0].Id);
        Assert.Equal(alpha.Id, recent.Items[0].Id);
        Assert.Equal((1, 10), (defaultPageSize.Page, defaultPageSize.PageSize));
        Assert.Equal(25, page25.PageSize);
        Assert.Equal(50, page50.PageSize);
        Assert.Empty(outOfRange.Items);
        Assert.Equal(3, outOfRange.TotalCount);
    }

    [Fact]
    public async Task VehicleSummariesStayBatchedAndOrderedAcrossClientPages()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var clients = Enumerable.Range(0, 12)
            .Select(index => ClientsDatabaseFixture.CreateClient(organization, $"Клиент {index:D2}"))
            .ToArray();
        seedContext.OrganizationClients.AddRange(clients);
        await seedContext.SaveChangesAsync();

        foreach (var client in clients)
        {
            var second = await fixture.AddVehicleAsync(seedContext, organization, client, "Бета");
            var first = await fixture.AddVehicleAsync(seedContext, organization, client, "Альфа");
            first.IsArchived = true;
            first.YearIssue = 2020;
            first.RegistrationPlate = "А123АА154";
            await fixture.AddVehicleAsync(seedContext, organization, client, "Удалённый", isDeleted: true);
            seedContext.Maintenance.Add(ClientsDatabaseFixture.CreateMaintenance(
                organization, second, MaintenanceStatus.Planned, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
        }

        await seedContext.SaveChangesAsync();
        var counter = new ClientsDatabaseFixture.SqlCaptureInterceptor();
        await using var context = fixture.CreateContext(counter);
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var firstPage = await query.GetPageAsync(UserId, organization.Id, null, "name_asc", 1, 10);
        var firstPageQueryCount = counter.Commands.Count;
        counter.Commands.Clear();
        var secondPage = await query.GetPageAsync(UserId, organization.Id, null, "name_asc", 2, 10);

        Assert.Equal(10, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(firstPageQueryCount, counter.Commands.Count);

        foreach (var client in firstPage.Items.Concat(secondPage.Items))
        {
            Assert.Equal(2, client.Vehicles.Count);
            Assert.Contains("Альфа", client.Vehicles[0].DisplayName, StringComparison.Ordinal);
            Assert.Contains("2020 г.", client.Vehicles[0].DisplayName, StringComparison.Ordinal);
            Assert.Equal("А123АА154", client.Vehicles[0].RegistrationPlate);
            Assert.Equal(0, client.Vehicles[0].MaintenanceCount);
            Assert.Null(client.Vehicles[0].LastMaintenanceDate);
            Assert.Contains("Бета", client.Vehicles[1].DisplayName, StringComparison.Ordinal);
            Assert.Equal(1, client.Vehicles[1].MaintenanceCount);
            Assert.Null(client.Vehicles[1].LastMaintenanceDate);
        }

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task QueryCountDoesNotGrowWithOrganizationSizeAndGetByIdFiltersInSql()
    {
        var commandCounter = new ClientsDatabaseFixture.SqlCaptureInterceptor();
        await using var seedContext = fixture.CreateContext();
        var singleClientOrganization = await fixture.CreateOrganizationAsync(seedContext, "Пространство с одним клиентом");
        var manyClientOrganization = await fixture.CreateOrganizationAsync(seedContext, "Пространство с несколькими клиентами");
        var singleClient = ClientsDatabaseFixture.CreateClient(singleClientOrganization, "Единственный клиент");
        var clients = Enumerable.Range(0, 12)
            .Select(index => ClientsDatabaseFixture.CreateClient(manyClientOrganization, $"Клиент {index:D2}"))
            .ToArray();
        seedContext.OrganizationClients.Add(singleClient);
        seedContext.OrganizationClients.AddRange(clients);
        await seedContext.SaveChangesAsync();

        await using var context = fixture.CreateContext(commandCounter);
        using var services = fixture.CreateServices(context);
        var query = services.GetRequiredService<IOrganizationClientQueryService>();

        var singleResult = await query.GetByIdAsync(UserId, singleClientOrganization.Id, singleClient.Id);
        var singleClientQueryCount = commandCounter.Commands.Count;
        var result = await query.GetByIdAsync(UserId, manyClientOrganization.Id, clients[4].Id);
        var manyClientQueryCount = commandCounter.Commands.Count - singleClientQueryCount;

        Assert.Equal(singleClient.Id, singleResult?.Id);
        Assert.Equal(clients[4].Id, result?.Id);
        Assert.InRange(singleClientQueryCount, 1, 8);
        Assert.Equal(singleClientQueryCount, manyClientQueryCount);
        var clientQueries = commandCounter.Commands
            .Where(command => command.Contains("\"OrganizationClients\"", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, clientQueries.Length);

        foreach (var clientQuery in clientQueries)
        {
            var whereClause = clientQuery[clientQuery.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase)..];
            Assert.Contains("\"Id\" =", whereClause, StringComparison.Ordinal);
            Assert.Contains("@clientId", whereClause, StringComparison.Ordinal);
        }
    }
}
