using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Imports;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed partial class OrganizationImportBatchTests
{
    private const string BusinessSql = """
        INSERT INTO clients (clientId, clientName, clientPhone) VALUES ('c1', 'Анна', '79990000001');
        INSERT INTO cars (vehicleId, vin, vehicleClientId) VALUES ('v1', 'AAA12345678', 'c1');
        INSERT INTO orders (orderId, vehicleExternalId, occurredAt, orderStatus, orderType, amount) VALUES ('o1', 'v1', '2020-02-01T10:00:00Z', 'completed', 'repair', '2000');
        INSERT INTO lines (orderExternalId, detailName, quantity, unitPrice) VALUES ('o1', 'Полировка', '2', '1000');
        INSERT INTO payments (orderExternalId, amount, occurredAt, paymentMethod) VALUES ('o1', '500', '2020-02-01T11:00:00Z', 'cash');
        INSERT INTO events (orderExternalId, occurredAt, eventType, historyComment) VALUES ('o1', '2020-02-01T12:00:00Z', 'completed', 'Исходная история');
        """;

    [Fact]
    public async Task SourceModeRejectsGeneratedRowOrPhoneIdsWhileIdlessDirectoryImportStillWorks()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var draft = await service.UploadAsync(userId, organizationId, "combined.csv", Encoding.UTF8.GetBytes(
            "clientName;clientPhone;vehicleId;plate\nАнна;79990000001;v1;A123AA"), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        await service.SetSourceAsync(userId, organizationId, draft.Id, draft.Revision, "CRM", default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.Equal("error", draft.Data.Rows.Single(row => row.Kind == "client").Status);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default));
        await service.SetSourceAsync(userId, organizationId, draft.Id, draft.Revision, null, default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        Assert.All(draft.Data.Rows, row => Assert.Equal("create", row.Status));
        draft = await CompleteSourceAsync(service, userId, organizationId, draft);

        Assert.Equal("completed", draft.State);
        Assert.Equal(1, draft.Result!.CreatedClients);
        Assert.Equal(1, draft.Result.CreatedVehicles);
    }

    [Fact]
    public async Task SourceIdentitySkipsWholeRepeatedOrderAndRejectsChangedPayment()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var first = await PrepareSourceAsync(service, userId, organizationId, BusinessSql);
        Assert.All(first.Data.Rows, row => Assert.Equal("create", row.Status));
        first = await CompleteSourceAsync(service, userId, organizationId, first);
        Assert.Equal("completed", first.State);
        Assert.Equal(3, await context.Set<OrganizationImportIdentity>().CountAsync(identity => identity.OrganizationId == organizationId));

        var second = await PrepareSourceAsync(service, userId, organizationId, BusinessSql);
        Assert.Equal("link", second.Data.Rows.Single(row => row.Kind == "client").Status);
        Assert.All(second.Data.Rows.Where(row => row.Kind != "client"), row => Assert.True(row.Status == "duplicate", row.Message));
        second = await CompleteSourceAsync(service, userId, organizationId, second);
        Assert.Equal("completed", second.State);
        Assert.Equal(0, second.Result!.CreatedOrders);
        var vehicle = await context.Vehicles.SingleAsync(vehicle => vehicle.OrganizationId == organizationId);
        var order = await context.Maintenance.SingleAsync(order => order.VehicleId == vehicle.Id);
        Assert.Equal(1, await context.MaintenancePayments.CountAsync(payment => payment.MaintenanceId == order.Id));
        Assert.Equal(2, await context.MaintenanceHistoryEvents.CountAsync(item => item.MaintenanceId == order.Id));

        var changed = await PrepareSourceAsync(service, userId, organizationId, BusinessSql.Replace("'500'", "'600'"));
        Assert.Equal("conflict", changed.Data.Rows.Single(row => row.Kind == "order").Status);
        Assert.All(changed.Data.Rows.Where(row => row.Kind is "order-line" or "payment" or "history"), row => Assert.Equal("conflict", row.Status));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.StartAsync(userId, organizationId, changed.Id, changed.Revision, false, default));
        Assert.Equal(500m, (await context.MaintenancePayments.SingleAsync(payment => payment.MaintenanceId == order.Id)).Amount);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SetSourceAsync(userId, organizationId, first.Id, first.Revision, "other", default));

        var invalidChanged = await PrepareSourceAsync(service, userId, organizationId, BusinessSql
            .Replace("'500'", "'3000'")
            .Replace("'2020-02-01T11:00:00Z'", "'invalid-date'"));

        Assert.All(invalidChanged.Data.Rows.Where(row => row.Kind is "order" or "order-line" or "payment" or "history"), row =>
        {
            Assert.Equal("conflict", row.Status);
            Assert.Equal(row.Kind == "order" ? ImportRowMessageCodes.IdentityConflict : ImportRowMessageCodes.ChildIdentityConflict, row.MessageCode);
            Assert.Null(row.MessageDetails);
        });
    }

    [Fact]
    public async Task ExternalIdLengthErrorReplacesEarlierBusinessValidationDetails()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var sql = BusinessSql.Replace("'o1'", $"'{new string('x', 101)}'").Replace("'500'", "'3000'");

        var draft = await PrepareSourceAsync(service, userId, organizationId, sql);
        var order = draft.Data.Rows.Single(row => row.Kind == "order");

        Assert.Equal("error", order.Status);
        Assert.Equal(ImportRowMessageCodes.ExternalIdentityTooLong, order.MessageCode);
        Assert.Null(order.MessageDetails);
    }

    [Fact]
    public async Task SourceIdentityKeepsLegacyRussianVehicleFingerprintAcrossCountryFieldAddition()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        const string legacySql = "INSERT INTO cars (vehicleId, vin, plate) VALUES ('v1', 'VIN123', 'A123AA');";
        const string explicitRussianCountrySql = "INSERT INTO cars (vehicleId, vin, plate, registrationCountryCode) VALUES ('v1', 'VIN123', 'A123AA', 'RU');";

        var first = await PrepareSourceAsync(service, userId, organizationId, legacySql);
        first = await CompleteSourceAsync(service, userId, organizationId, first);
        var importedVehicle = Assert.Single(first.Data.Rows, row => row.Kind == "vehicle");
        var externalId = importedVehicle.ExternalId!.Trim().ToUpperInvariant();
        var identity = await context.Set<OrganizationImportIdentity>().SingleAsync(item =>
            item.OrganizationId == organizationId && item.SourceKey == first.SourceKey && item.Kind == "vehicle" && item.ExternalId == externalId);
        identity.Fingerprint = LegacyFingerprint(importedVehicle);
        await context.SaveChangesAsync();

        var repeated = await PrepareSourceAsync(service, userId, organizationId, explicitRussianCountrySql);

        Assert.Equal("duplicate", repeated.Data.Rows.Single(row => row.Kind == "vehicle").Status);
        repeated = await CompleteSourceAsync(service, userId, organizationId, repeated);
        Assert.Equal("completed", repeated.State);
        Assert.Equal(0, repeated.Result!.CreatedVehicles);
    }

    [Fact]
    public async Task ForeignRegistrationCountryChangesSourceFingerprint()
    {
        var (firstUserId, firstOrganizationId) = await this.SeedAsync();
        var (secondUserId, secondOrganizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        const string sqlTemplate = "INSERT INTO cars (vehicleId, vin, plate, registrationCountryCode) VALUES ('v1', 'VIN123', '京A·2', '{0}');";

        var first = await PrepareSourceAsync(CreateService(context), firstUserId, firstOrganizationId, string.Format(sqlTemplate, "CN"));
        await CompleteSourceAsync(CreateService(context), firstUserId, firstOrganizationId, first);
        var firstFingerprint = await context.Set<OrganizationImportIdentity>()
            .Where(item => item.OrganizationId == firstOrganizationId && item.Kind == "vehicle")
            .Select(item => item.Fingerprint).SingleAsync();

        var second = await PrepareSourceAsync(CreateService(context), secondUserId, secondOrganizationId, string.Format(sqlTemplate, "ZZ"));
        await CompleteSourceAsync(CreateService(context), secondUserId, secondOrganizationId, second);
        var secondFingerprint = await context.Set<OrganizationImportIdentity>()
            .Where(item => item.OrganizationId == secondOrganizationId && item.Kind == "vehicle")
            .Select(item => item.Fingerprint).SingleAsync();

        Assert.NotEqual(firstFingerprint, secondFingerprint);
    }

    [Fact]
    public async Task SourceIdentityResolvesPriorClientsAndCarsWhenNewFileContainsOnlyNewOrders()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var first = await PrepareSourceAsync(service, userId, organizationId, BusinessSql);
        await CompleteSourceAsync(service, userId, organizationId, first);
        var ordersOnly = string.Join('\n', BusinessSql.Split('\n').Where(line => !line.StartsWith("INSERT INTO clients") && !line.StartsWith("INSERT INTO cars")))
            .Replace("'o1'", "'o2'");
        var next = await PrepareSourceAsync(service, userId, organizationId, ordersOnly);
        Assert.All(next.Data.Rows, row => Assert.True(row.Status == "create", row.Message));
        next = await CompleteSourceAsync(service, userId, organizationId, next);
        Assert.Equal("completed", next.State);
        Assert.Equal(1, next.Result!.CreatedOrders);
        Assert.Equal(1, await context.Vehicles.CountAsync(vehicle => vehicle.OrganizationId == organizationId));
        Assert.Equal(1, await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
    }

    [Fact]
    public async Task SourceIdentityRechecksConcurrentPackagesAndDeletedTargets()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var first = await PrepareSourceAsync(service, userId, organizationId, BusinessSql);
        var preparedEarlier = await PrepareSourceAsync(service, userId, organizationId, BusinessSql);
        await CompleteSourceAsync(service, userId, organizationId, first);
        await service.StartAsync(userId, organizationId, preparedEarlier.Id, preparedEarlier.Revision, false, default);
        await service.ProcessPendingAsync(default);
        var rechecked = await PageAsync(service, userId, organizationId, preparedEarlier.Id);
        Assert.Equal("ready", rechecked.State);
        Assert.NotNull(rechecked.Error);
        Assert.Equal("duplicate", rechecked.Data.Rows.Single(row => row.Kind == "order").Status);
        var vehicle = await context.Vehicles.SingleAsync(vehicle => vehicle.OrganizationId == organizationId);
        vehicle.IsDeleted = true;
        await context.SaveChangesAsync();
        var deleted = await PrepareSourceAsync(service, userId, organizationId, BusinessSql);
        Assert.Equal("conflict", deleted.Data.Rows.Single(row => row.Kind == "vehicle").Status);
        Assert.Equal("conflict", deleted.Data.Rows.Single(row => row.Kind == "order").Status);
    }

    [Fact]
    public async Task RepeatedLargeOrderRemainsAnAtomicAggregateAcrossApplyPages()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var lines = string.Join(",", Enumerable.Range(1, 300).Select(index => $"('o1', 'Работа {index}', '1', '10')"));
        var sql = BusinessSql.Replace("('o1', 'Полировка', '2', '1000')", lines).Replace("'2000'", "'3000'");
        var first = await PrepareSourceAsync(service, userId, organizationId, sql);
        first = await CompleteSourceAsync(service, userId, organizationId, first);
        Assert.Equal("completed", first.State);
        Assert.Equal(300, first.Result!.CreatedOrderLines);
        var second = await PrepareSourceAsync(service, userId, organizationId, sql);
        second = await CompleteSourceAsync(service, userId, organizationId, second);

        Assert.Equal("completed", second.State);
        Assert.Equal(0, second.Result!.CreatedOrders);
        Assert.Equal(0, second.Result.CreatedPayments);
        Assert.Equal(0, second.Result.CreatedOrderLines);
        Assert.Equal(305, second.ProcessedRows);
    }

    [Fact]
    public async Task ConcurrentSameNameProfileSavesKeepSingleProfile()
    {
        var (userId, organizationId) = await this.SeedAsync();
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var profile = new ImportProfileDto
        {
            Name = "Один профиль", Tables = [new() { Id = "clients", Name = "Клиенты", Columns = ["Имя"] }],
            Mappings = [new() { TableId = "clients", Kind = "client", Fields = new() { ["clientName"] = [0] } }]
        };
        var saved = await Task.WhenAll(
            CreateService(firstContext).SaveProfileAsync(userId, organizationId, profile, default),
            CreateService(secondContext).SaveProfileAsync(userId, organizationId, profile, default));

        Assert.Equal(saved[0].Id, saved[1].Id);
        Assert.Single(await CreateService(firstContext).GetProfilesAsync(userId, organizationId, default));
    }

    [Fact]
    public async Task ProfilesStripSourceRowsAndRemainPrivateToAuthorAndOrganization()
    {
        var (userId, organizationId) = await this.SeedAsync();
        var (_, otherOrganizationId) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var table = new ImportSourceTable
        {
            Id = "clients", Name = "Клиенты", Columns = ["Имя"],
            Rows = [new ImportSourceRow { Number = 2, Values = ["Секретный клиент"] }]
        };
        var saved = await service.SaveProfileAsync(userId, organizationId, new ImportProfileDto
        {
            Name = "Мой профиль", SourceKey = " База 1 ", Tables = [table],
            Mappings = [new ImportTableMapping { TableId = table.Id, Kind = "client", Fields = new() { ["clientName"] = [0] } }]
        }, default);
        Assert.Equal("БАЗА 1", saved.SourceKey);
        Assert.Empty(Assert.Single(saved.Tables).Rows);
        Assert.DoesNotContain("Секретный клиент", (await context.Set<OrganizationImportProfile>().SingleAsync(profile => profile.Id == saved.Id)).SettingsJson);
        Assert.Single(await service.GetProfilesAsync(userId, organizationId, default));
        Assert.Empty(await service.GetProfilesAsync("other-user", organizationId, default));
        Assert.Empty(await service.GetProfilesAsync(userId, otherOrganizationId, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteProfileAsync("other-user", organizationId, saved.Id, default));
        await service.DeleteProfileAsync(userId, organizationId, saved.Id, default);
        Assert.Empty(await service.GetProfilesAsync(userId, organizationId, default));
    }

    private static async Task<ImportDraftDto> PrepareSourceAsync(
        OrganizationImportService service, string userId, Guid organizationId, string sql)
    {
        var draft = await service.UploadAsync(userId, organizationId, "source.sql", Encoding.UTF8.GetBytes(sql), default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        await service.SetSourceAsync(userId, organizationId, draft.Id, draft.Revision, "Моя CRM — база 1", default);
        await service.ProcessPendingAsync(default);
        draft = await PageAsync(service, userId, organizationId, draft.Id);
        var kinds = new Dictionary<string, string>
        {
            ["clients"] = "client", ["cars"] = "vehicle", ["orders"] = "order", ["lines"] = "order-line",
            ["payments"] = "payment", ["events"] = "history"
        };
        var mappings = draft.Data.Tables.Select(table => new ImportTableMapping
        {
            TableId = table.Id, Kind = kinds[table.Name],
            Fields = table.Columns.Select((column, position) => (column, position))
                .ToDictionary(item => item.column, item => new[] { item.position })
        }).ToList();
        await service.QueueMappingAsync(userId, organizationId, draft.Id, draft.Revision, mappings, default);
        await service.ProcessPendingAsync(default);

        return await PageAsync(service, userId, organizationId, draft.Id);
    }

    private static string LegacyFingerprint(ImportCandidate row)
    {
        var editableFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name", "Excluded", "ExternalId", "ClientExternalId", "ClientPhone", "Phone", "Comment", "Brand", "Model", "Vin", "Plate", "Year",
            "ExistingClientId", "CreateSeparateClient", "PartyName", "VehicleExternalId", "OrderExternalId", "OccurredAt", "OrderStatus", "OrderType",
            "DetailDescription", "Quantity", "UnitPrice", "Amount", "DiscountAmount", "PaymentMethod", "CancelledAt", "ActorName", "EventType", "OrderComment", "HistoryComment"
        };
        var values = typeof(ImportCandidate).GetProperties()
            .Where(property => editableFields.Contains(property.Name) && property.Name is not ("Excluded" or "ExistingClientId" or "CreateSeparateClient"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(property => property.Name, property => property.GetValue(row));
        values["Kind"] = row.Kind;

        var semanticValue = JsonSerializer.Serialize(values);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { semanticValue }))));
    }

    private static async Task<ImportDraftDto> CompleteSourceAsync(
        OrganizationImportService service, string userId, Guid organizationId, ImportDraftDto draft)
    {
        await service.StartAsync(userId, organizationId, draft.Id, draft.Revision, false, default);
        for (var step = 0; step < 10; step++)
        {
            await service.ProcessPendingAsync(default);
            draft = await PageAsync(service, userId, organizationId, draft.Id);
            if (draft.State is "completed" or "failed" or "ready" or "paused")
            {
                break;
            }
        }

        return draft;
    }
}
