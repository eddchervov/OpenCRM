using System.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Imports;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed partial class OrganizationImportBatchTests(ImportDatabaseFixture fixture) : IClassFixture<ImportDatabaseFixture>
{
    [Fact]
    public async Task TenThousandClientsAreAppliedInBoundedBatchesAndPreviewRemainsPaged()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var csv = "ClientId;Имя;Телефон\n" + string.Join('\n', Enumerable.Range(1, 10000)
            .Select(index => $"c{index};Масштабный клиент {index};7998{index:D7}"));
        var draft = await service.UploadAsync(userId, organizationId, "large.csv", Encoding.UTF8.GetBytes(csv), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id, page: 200);
        Assert.Equal(10000, draft.TotalRows);
        Assert.Equal(50, draft.Data.Rows.Count);
        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        for (var step = 0; step < 40; step++)
        {
            await service.ProcessPendingAsync(default);
        }
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("completed", draft.State);
        Assert.Equal(10000, draft.ProcessedRows);
        Assert.Equal(10000, draft.Result!.CreatedClients);
        Assert.Equal(100, draft.Result.ClientIds.Count);
        Assert.Equal(10000, await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
        Assert.Equal(50, draft.Data.Rows.Count);
    }

    [Fact]
    public async Task PagesPatchAndRestartedWorkerResumeWithoutDuplicatingAppliedRows()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var csv = "ClientId;Имя;Телефон\n" + string.Join('\n', Enumerable.Range(1, 600).Select(index => $"c{index};Клиент {index};7999{index:D7}"));
        var draft = await service.UploadAsync(userId, organizationId, "clients.csv", Encoding.UTF8.GetBytes(csv), default);
        Assert.Equal("upload-queued", draft.State);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal(20, draft.Data.Tables.Single().Rows.Count);
        draft = await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id, page: 12);
        Assert.Equal(600, draft.TotalRows);
        Assert.Equal(50, draft.Data.Rows.Count);
        Assert.Equal(600, draft.Counts["create"]);
        var changed = draft.Data.Rows.Last();
        changed.Name = "Исправленный клиент";
        var previousRevision = draft.Revision;
        await service.PatchRowsAsync(userId, organizationId, draft.Id, draft.Revision, [changed], default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal(600, draft.TotalRows);
        Assert.Equal(1, (await service.GetPageAsync(userId, organizationId, draft.Id, 1, 50, null, null, "Исправленный", default)).FilteredRows);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PatchRowsAsync(userId, organizationId, draft.Id, previousRevision, [changed], default));
        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal(250, draft.ProcessedRows);
        Assert.Equal("importing", draft.State);
        await service.PauseAsync(userId, organizationId, draft.Id, draft.Revision, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("paused", draft.State);
        Assert.Equal(250, await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PatchRowsAsync(userId, organizationId, draft.Id, draft.Revision, [draft.Data.Rows[0]], default));

        await using var restartedContext = fixture.CreateContext();
        var restarted = CreateService(restartedContext);
        await restarted.StartAsync(userId, organizationId, draft.Id, draft.Revision, true, default);
        await restarted.ProcessPendingAsync(default);
        await restarted.ProcessPendingAsync(default);
        draft = await PageAsync(restarted, userId, organizationId, draft.Id);
        Assert.Equal("completed", draft.State);
        Assert.Equal(600, draft.ProcessedRows);
        Assert.Equal(600, draft.Result!.CreatedClients);
        Assert.Equal(600, await restartedContext.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
        Assert.True(await restartedContext.OrganizationClients.AnyAsync(client => client.OrganizationId == organizationId && client.Name == "Исправленный клиент"));
        await restarted.StartAsync(userId, organizationId, draft.Id, draft.Revision, true, default);
        Assert.Equal(600, await restartedContext.Set<OrganizationImportRow>().CountAsync(row => row.DraftId == draft.Id && row.AppliedAt != null));
    }

    [Fact]
    public async Task NewClientBetweenImportPagesStopsNextPageBeforeCreatingClientsOrVehicles()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context, maxVehicles: 500, maxClients: 500);
        var csv = "ClientId;Имя;Телефон;vehicleId;Марка;Модель;VIN;Госномер\n" + string.Join('\n', Enumerable.Range(1, 500)
            .Select(index => $"c{index};Клиент {index};7999{index:D7};v{index};Toyota;Camry;VIN{index:D14};A{index % 1000:D3}AA"));
        var draft = await service.UploadAsync(userId, organizationId, "clients-and-vehicles.csv", Encoding.UTF8.GetBytes(csv), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        draft = await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal(1000, draft.TotalRows);
        Assert.Equal(1000, draft.Counts["create"]);

        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("importing", draft.State);
        Assert.Equal(250, draft.ProcessedRows);

        await using (var concurrentContext = fixture.CreateContext())
        {
            concurrentContext.OrganizationClients.AddRange(Enumerable.Range(1, 249).Select(index => new OrganizationClient
            {
                OrganizationId = organizationId,
                Name = $"Внешний клиент {index}"
            }));
            await concurrentContext.SaveChangesAsync();
        }

        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("paused", draft.State);
        Assert.Contains("лимит клиентов", draft.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(250, draft.ProcessedRows);
        Assert.Equal(250, draft.Result!.CreatedClients);
        Assert.Equal(0, draft.Result.CreatedVehicles);
        Assert.Equal(250, await context.Set<OrganizationImportRow>().CountAsync(row => row.DraftId == draft.Id && row.AppliedAt != null));
        Assert.Equal(499, await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
        Assert.Empty(await context.Vehicles.Where(vehicle => vehicle.OrganizationId == organizationId).ToListAsync());

        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, true, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("paused", draft.State);
        Assert.Equal(250, draft.ProcessedRows);
        Assert.Equal(250, draft.Result!.CreatedClients);
        Assert.Equal(0, draft.Result.CreatedVehicles);
        Assert.Equal(499, await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
        Assert.Empty(await context.Vehicles.Where(vehicle => vehicle.OrganizationId == organizationId).ToListAsync());
    }

    [Fact]
    public async Task NewVehicleBetweenImportPagesStopsNextPageWithoutRepeatingAppliedVehicles()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context, maxVehicles: 500);
        var csv = "vehicleId;Марка;Модель;VIN;Госномер\n" + string.Join('\n', Enumerable.Range(1, 500)
            .Select(index => $"v{index};Toyota;Camry;VIN{index:D14};A{index % 1000:D3}AA"));
        var draft = await service.UploadAsync(userId, organizationId, "vehicles.csv", Encoding.UTF8.GetBytes(csv), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        draft = await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal(500, draft.TotalRows);
        Assert.Equal(500, draft.Counts["create"]);

        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("importing", draft.State);
        Assert.Equal(250, draft.ProcessedRows);

        await using (var concurrentContext = fixture.CreateContext())
        {
            concurrentContext.Vehicles.AddRange(Enumerable.Range(1, 250).Select(index => new Vehicle
            {
                OrganizationId = organizationId,
                UserId = userId,
                ManualBrandName = "Внешний производитель",
                ManualModelName = $"Модель {index}",
                Vin = $"EXT{index:D14}",
                RegistrationPlate = $"B{index % 1000:D3}BB",
                DateTimeCreation = DateTime.UtcNow
            }));
            await concurrentContext.SaveChangesAsync();
        }

        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("paused", draft.State);
        Assert.Contains("лимит техники", draft.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(250, draft.ProcessedRows);
        Assert.Equal(250, draft.Result!.CreatedVehicles);
        Assert.Equal(250, await context.Set<OrganizationImportRow>().CountAsync(row => row.DraftId == draft.Id && row.AppliedAt != null));
        Assert.Equal(500, await context.Vehicles.CountAsync(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted));

        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, true, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("paused", draft.State);
        Assert.Equal(250, draft.ProcessedRows);
        Assert.Equal(250, draft.Result!.CreatedVehicles);
        Assert.Equal(500, await context.Vehicles.CountAsync(vehicle => vehicle.OrganizationId == organizationId && !vehicle.IsDeleted));
    }

    [Fact]
    public async Task SqlTablesImportOrderLinesPaymentsAndHistoricalEventsThroughRealForeignKeys()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var sql = """
            INSERT INTO clients (clientId, clientName, clientPhone) VALUES ('c1', 'Анна', '79990000001');
            INSERT INTO cars (vehicleId, vin, vehicleClientId) VALUES ('v1', 'AAA12345678', 'c1');
            INSERT INTO orders (orderId, vehicleExternalId, occurredAt, orderStatus, orderType, amount, discountAmount, partyClientId, partyName) VALUES ('o1', 'v1', '2020-02-01T10:00:00Z', 'completed', 'repair', '1500', '500', 'c1', 'Анна в 2020 году');
            INSERT INTO lines (orderExternalId, detailName, quantity, unitPrice) VALUES ('o1', 'Полировка', '2', '1000');
            INSERT INTO payments (orderExternalId, amount, occurredAt, paymentMethod, actorName, partyClientId, partyName) VALUES ('o1', '500', '2020-02-01T11:00:00Z', 'cash', 'Кассир старой CRM', 'c1', 'Исторический плательщик');
            INSERT INTO events (orderExternalId, occurredAt, eventType, historyComment, actorName) VALUES ('o1', '2020-02-01T12:00:00Z', 'completed', 'Исходная история', 'Мастер старой CRM');
            """;
        var draft = await service.UploadAsync(userId, organizationId, "data.sql", Encoding.UTF8.GetBytes(sql), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        string[] kinds = ["client", "vehicle", "order", "order-line", "payment", "history"];
        var mappings = draft.Data.Tables.Select((table, index) => new ImportTableMapping
        {
            TableId = table.Id, Kind = kinds[index],
            Fields = table.Columns.Select((column, position) => (column, position)).ToDictionary(item => item.column, item => new[] { item.position })
        }).ToList();
        await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("ready", draft.State);
        Assert.All(draft.Data.Rows, row => Assert.True(row.Status == "create", row.Message));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default));
        await service.SetSourceAsync(userId, organizationId, draft.Id, draft.Revision, "Историческая CRM", default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        for (var step = 0; step < 4; step++) await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("completed", draft.State);
        Assert.Equal(1, draft.Result!.CreatedOrders);
        Assert.Equal(1, draft.Result.CreatedPayments);
        var vehicle = await context.Vehicles.SingleAsync(vehicle => vehicle.OrganizationId == organizationId);
        var order = await context.Maintenance.Include(order => order.Details).Include(order => order.HistoryEvents)
            .SingleAsync(order => order.VehicleId == vehicle.Id);
        var payment = await context.MaintenancePayments.SingleAsync(payment => payment.MaintenanceId == order.Id);
        Assert.Equal(500m, order.PaidAmount);
        Assert.Equal(500m, order.DiscountAmount);
        Assert.Equal(2, Assert.Single(order.Details).Quantity);
        Assert.Equal(new DateTime(2020, 2, 1, 11, 0, 0, DateTimeKind.Utc), payment.CreatedAt);
        Assert.Equal(userId, payment.CreatedBy);
        Assert.Equal(vehicle.OrganizationClientId, order.BuyerClientId);
        Assert.Equal("Анна в 2020 году", order.BuyerName);
        Assert.Equal("Исторический плательщик", payment.PayerName);
        Assert.Contains(order.HistoryEvents, item => item.EventType == "imported-history" && item.Description == "Исходная история");
        Assert.DoesNotContain(order.HistoryEvents, item => item.EventType == "completed");
        Assert.All(draft.Data.Rows, row => Assert.NotNull(row.AppliedAt));
        Assert.All(draft.Data.Rows, row => Assert.NotNull(row.TargetId));
    }

    [Fact]
    public async Task InvalidSourceFailsInBackgroundAndForeignOrganizationCannotReadOrEditBatch()
    {
        var (userId, organizationId) = await this.SeedAsync();
        var (_, foreignId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var draft = await service.UploadAsync(userId, organizationId, "unsafe.sql", Encoding.UTF8.GetBytes("DROP TABLE clients;"), default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => PageAsync(service, userId, foreignId, draft.Id));
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("failed", draft.State);
        Assert.Contains("не поддерживается", draft.Error);
        Assert.False(await context.OrganizationClients.AnyAsync(client => client.OrganizationId == organizationId));
    }

    [Fact]
    public async Task NormalizedExistingVehicleGetsTargetWithoutCreatingDuplicate()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var vehicle = new Vehicle { OrganizationId = organizationId, UserId = userId, RegistrationPlate = "А 001-АА", DateTimeCreation = DateTime.UtcNow };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var draft = await service.UploadAsync(userId, organizationId, "cars.csv", Encoding.UTF8.GetBytes(
            "ClientId;Имя;Телефон;vehicleId;Госномер\nc1;Новый клиент;79990000001;v1;А001АА"), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("duplicate", draft.Data.Rows.Single(row => row.Kind == "vehicle").Status);
        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        await service.ProcessPendingAsync(default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("completed", draft.State);
        Assert.Equal(vehicle.Id, draft.Data.Rows.Single(row => row.Kind == "vehicle").TargetId);
        Assert.Equal(1, await context.Vehicles.CountAsync(item => item.OrganizationId == organizationId));
    }

    [Fact]
    public async Task BatchImportMapsAndPersistsForeignRegistrationCountry()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var draft = await service.UploadAsync(userId, organizationId, "cars.csv", Encoding.UTF8.GetBytes(
            "Название автомобиля;VIN;Госномер;Страна регистрации\nCar;VIN123;京A·2;cn"), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        draft = await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        var candidate = Assert.Single(draft.Data.Rows);
        Assert.Equal("CN", candidate.RegistrationCountryCode);
        Assert.Equal("京A·2", candidate.Plate);
        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("completed", draft.State);
        var vehicle = await context.Vehicles.SingleAsync(item => item.OrganizationId == organizationId);
        Assert.Equal("CN", vehicle.RegistrationCountryCode);
        Assert.Equal("京A·2", vehicle.RegistrationPlate);
    }

    [Fact]
    public async Task BatchDuplicateLinkUsesCountryAndRawForeignPlateKey()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var existing = new Vehicle
        {
            OrganizationId = organizationId,
            UserId = userId,
            Vin = "VIN123",
            RegistrationCountryCode = "CN",
            RegistrationPlate = "京A·2",
            DateTimeCreation = DateTime.UtcNow
        };
        context.Vehicles.Add(existing);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var draft = await service.UploadAsync(userId, organizationId, "cars.csv", Encoding.UTF8.GetBytes(
            "Название автомобиля;VIN;Госномер;Страна регистрации\nCar;VIN123;京A·2;cn"), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        draft = await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("duplicate", Assert.Single(draft.Data.Rows).Status);

        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("completed", draft.State);
        Assert.Equal(existing.Id, Assert.Single(draft.Data.Rows).TargetId);
        Assert.Equal(1, await context.Vehicles.CountAsync(item => item.OrganizationId == organizationId));
    }

    [Fact]
    public async Task InvalidMappingCanBeCorrectedWithoutUploadingSourceAgain()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var csv = "Имя;Часть1;Часть2\nАнна;" + new string('а', 6000) + ";" + new string('б', 6000);
        var draft = await service.UploadAsync(userId, organizationId, "long.csv", Encoding.UTF8.GetBytes(csv), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        draft.Data.Mappings[0].Fields["clientName"] = [1, 2];
        await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("ready", draft.State);
        Assert.NotNull(draft.Error);
        Assert.Equal(0, draft.TotalRows);
        draft.Data.Mappings[0].Fields["clientName"] = [0];
        await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);

        Assert.Equal("create", Assert.Single(draft.Data.Rows).Status);
        Assert.Null(draft.Error);
    }

    [Fact]
    public async Task ImporterWithoutBusinessPermissionsCannotReadOrMutateAnotherUsersBatch()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        subscriptions.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto());
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync("limited", organizationId, OrganizationPermissionCodes.FinanceView))
            .ThrowsAsync(new UnauthorizedAccessException());
        var service = CreateService(context, subscriptions.Object);
        var draft = await service.UploadAsync(userId, organizationId, "private.csv", Encoding.UTF8.GetBytes("Имя\nАнна"), default);
        var legacy = await service.CreateAsync(userId, organizationId, "legacy.csv", Encoding.UTF8.GetBytes("Имя;Сумма\nАнна;1500"), default);

        Assert.Empty(await service.ListAsync("limited", organizationId, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync("limited", organizationId, legacy.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.MapAsync("limited", organizationId, legacy.Id, legacy.Revision, legacy.Data.Mappings, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync("limited", organizationId, legacy.Id, legacy.Revision, [], default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConfirmAsync("limited", organizationId, legacy.Id, legacy.Revision, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PageAsync(service, "limited", organizationId, draft.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetReportAsync("limited", organizationId, draft.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueueMappingAsync("limited", organizationId, draft.Id, draft.Revision, [], default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PatchRowsAsync("limited", organizationId, draft.Id, draft.Revision, [], default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StartAsync("limited", organizationId, draft.Id, draft.Revision, false, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PauseAsync("limited", organizationId, draft.Id, draft.Revision, default));
        Assert.Equal(draft.Revision, (await PageAsync(service, userId, organizationId, draft.Id)).Revision);
        await service.ProcessPendingAsync(default);
    }

    private async Task<(string UserId, Guid OrganizationId)> SeedAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Пакетный импорт", OwnerUser = user };
        organization.Members.Add(new OrganizationMember { User = user, RoleCode = "owner" });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return (user.Id, organization.Id);
    }

    private static OrganizationImportService CreateService(
        OpenCRMContext context,
        IOrganizationSubscriptionService? subscriptionsOverride = null,
        int maxVehicles = 200000,
        int maxClients = int.MaxValue)
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        subscriptions.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto
            {
                Features = [new FeatureValueDto { Code = Helpers.FeatureCodes.WorkspaceClientsLimit, IntValue = maxClients }]
            });
        var limits = new Mock<IVehicleLimitQueryService>();
        limits.Setup(service => service.GetVehicleLimitInfoAsync(It.IsAny<string>(), It.IsAny<Guid?>()))
            .Returns(async (string _, Guid? organizationId) => new VehicleLimitInfoDto
            {
                CurrentVehicleCount = await context.Vehicles.CountAsync(vehicle =>
                    vehicle.OrganizationId == organizationId && !vehicle.IsDeleted),
                MaxVehiclesForUser = maxVehicles
            });

        return new OrganizationImportService(context, subscriptionsOverride ?? subscriptions.Object, limits.Object, new ImportBusinessRecordProcessor(context));
    }

    private static Task<ImportDraftDto> PageAsync(OrganizationImportService service, string userId, Guid organizationId, Guid id, int page = 1) =>
        service.GetPageAsync(userId, organizationId, id, page, 50, null, null, null, default);
}
