using System.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Modules.Imports;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Features.Dtos;
using Helpers;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed class OrganizationImportTests(ImportDatabaseFixture fixture) : IClassFixture<ImportDatabaseFixture>
{
    [Fact]
    public async Task DraftEditsAndConfirmCreateReviewedRecordsAndRepeatReturnsSameResult()
    {
        var (userId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var draft = await service.CreateAsync(userId, organizationId, "clients.csv",
            Encoding.UTF8.GetBytes("Код;Клиент;Телефон;Госномер\r\nc1;Иван;79990000001;А001АА"), default);
        Assert.Empty(draft.Data.Rows);
        Assert.False(await context.OrganizationClients.AnyAsync(client => client.OrganizationId == organizationId));
        draft.Data.Mappings[0].Fields["clientId"] = [0];
        draft = await service.MapAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        Assert.Equal(2, draft.Data.Rows.Count);
        draft.Data.Rows.Single(row => row.Kind == "client").Name = "Иван исправленный";
        draft = await service.SaveAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Rows, default);
        var confirmedRevision = draft.Revision;
        var result = await service.ConfirmAsync(userId, organizationId, draft.Id, confirmedRevision, default);
        var repeated = await service.ConfirmAsync(userId, organizationId, draft.Id, confirmedRevision, default);

        Assert.NotNull(result.ImportedAt);
        Assert.Equal(1, result.Result!.CreatedClients);
        Assert.Equal(1, result.Result.CreatedVehicles);
        Assert.Equal(result.Result.ClientIds, repeated.Result!.ClientIds);
        var client = await context.OrganizationClients.SingleAsync(client => client.OrganizationId == organizationId);
        Assert.Equal("Иван исправленный", client.Name);
        var vehicle = await context.Vehicles.SingleAsync(vehicle => vehicle.OrganizationId == organizationId);
        Assert.Equal(client.Id, vehicle.OrganizationClientId);
        Assert.Single(await context.Set<VehicleHistoryEvent>().Where(item => item.VehicleId == vehicle.Id).ToListAsync());
        Assert.Contains("Создано", Encoding.UTF8.GetString(await service.GetReportAsync(userId, organizationId, draft.Id, default)));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(userId, organizationId, draft.Id, result.Revision, draft.Data.Rows, default));
    }

    [Fact]
    public async Task DraftRevisionAndOrganizationBoundariesRejectStaleOrForeignChanges()
    {
        var (userId, organizationId) = await this.CreateOrganizationAsync();
        var (_, otherOrganizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var draft = await service.CreateAsync(userId, organizationId, "clients.csv", Encoding.UTF8.GetBytes("Имя;Телефон\nАнна;111"), default);
        var oldRevision = draft.Revision;
        draft = await service.MapAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(userId, organizationId, draft.Id, oldRevision, draft.Data.Rows, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(userId, otherOrganizationId, draft.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MapAsync(userId, otherOrganizationId, draft.Id, draft.Revision, draft.Data.Mappings, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ConfirmAsync(userId, otherOrganizationId, draft.Id, draft.Revision, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetReportAsync(userId, otherOrganizationId, draft.Id, default));
        Assert.Empty(await service.ListAsync(userId, otherOrganizationId, default));
    }

    [Fact]
    public async Task ChangedOrganizationBlocksConfirmationUntilPreviewIsReviewedAgain()
    {
        var (userId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var draft = await service.CreateAsync(userId, organizationId, "clients.csv", Encoding.UTF8.GetBytes("Имя;Телефон\nАнна;111"), default);
        draft = await service.MapAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        context.OrganizationClients.Add(new OrganizationClient { OrganizationId = organizationId, Name = "Другой клиент", Phone = "111" });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ConfirmAsync(userId, organizationId, draft.Id, draft.Revision, default));
        Assert.Equal(1, await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
        context.ChangeTracker.Clear();
        draft = await service.SaveAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Rows, default);
        var row = Assert.Single(draft.Data.Rows);
        Assert.Equal("conflict", row.Status);
        row.ExistingClientId = Assert.Single(row.Matches).Id;
        draft = await service.SaveAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Rows, default);
        var result = await service.ConfirmAsync(userId, organizationId, draft.Id, draft.Revision, default);

        Assert.Equal(0, result.Result!.CreatedClients);
        Assert.Equal(1, result.Result.LinkedClients);
    }

    [Fact]
    public async Task VehicleLimitIsCheckedForWholeBatchAndForgedServerStatusesAreIgnored()
    {
        var (userId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = CreateService(context, maxVehicles: 1);
        var draft = await service.CreateAsync(userId, organizationId, "cars.csv", Encoding.UTF8.GetBytes("Госномер\nА001АА\nА002АА"), default);
        draft = await service.MapAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        Assert.All(draft.Data.Rows, row => Assert.Equal("error", row.Status));
        draft.Data.Rows.ForEach(row => row.Status = "create");
        draft = await service.SaveAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Rows, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ConfirmAsync(userId, organizationId, draft.Id, draft.Revision, default));
        Assert.False(await context.Vehicles.AnyAsync(vehicle => vehicle.OrganizationId == organizationId));
        draft.Data.Rows[1].Excluded = true;
        draft = await service.SaveAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Rows, default);
        var result = await service.ConfirmAsync(userId, organizationId, draft.Id, draft.Revision, default);

        Assert.Equal(1, result.Result!.CreatedVehicles);
        Assert.Equal(1, result.Result.Skipped);
    }

    [Fact]
    public async Task ClientLimitRejectsWholeImportAndCountsArchivedClients()
    {
        var (userId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        context.OrganizationClients.Add(new OrganizationClient
        {
            OrganizationId = organizationId,
            Name = "Архивный клиент",
            IsDeleted = true
        });
        await context.SaveChangesAsync();
        var service = CreateService(context, maxClients: 1);
        var draft = await service.CreateAsync(userId, organizationId, "clients.csv",
            Encoding.UTF8.GetBytes("Имя;Телефон\nНовый клиент;79990000005"), default);
        draft = await service.MapAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);

        Assert.Equal("error", Assert.Single(draft.Data.Rows).Status);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ConfirmAsync(userId, organizationId, draft.Id, draft.Revision, default));
        Assert.Equal(1, await context.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
    }

    [Fact]
    public async Task RevokedPermissionsBlockReadingEditingReportsAndConfirm()
    {
        var (userId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var allowed = CreateService(context);
        var draft = await allowed.CreateAsync(userId, organizationId, "clients.csv", Encoding.UTF8.GetBytes("Имя\nАнна"), default);
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(userId, organizationId, It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        var denied = new OrganizationImportService(context, subscriptions.Object, Mock.Of<IVehicleLimitQueryService>());

        Assert.False(await denied.CanImportAsync(userId, organizationId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.GetAsync(userId, organizationId, draft.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.ListAsync(userId, organizationId, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.SaveAsync(userId, organizationId, draft.Id, draft.Revision, [], default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.ConfirmAsync(userId, organizationId, draft.Id, draft.Revision, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.GetReportAsync(userId, organizationId, draft.Id, default));
    }

    [Fact]
    public async Task ConcurrentConfirmationsCreateOnlyOneSetOfRecords()
    {
        var (userId, organizationId) = await this.CreateOrganizationAsync();
        ImportDraftDto draft;
        await using (var context = fixture.CreateContext())
        {
            var service = CreateService(context);
            draft = await service.CreateAsync(userId, organizationId, "clients.csv", Encoding.UTF8.GetBytes("Имя\nАнна"), default);
            draft = await service.MapAsync(userId, organizationId, draft.Id, draft.Revision, draft.Data.Mappings, default);
        }

        async Task<ImportDraftDto> Confirm()
        {
            await using var context = fixture.CreateContext();

            return await CreateService(context).ConfirmAsync(userId, organizationId, draft.Id, draft.Revision, default);
        }

        var results = await Task.WhenAll(Confirm(), Confirm());
        Assert.Equal(results[0].Result!.ClientIds, results[1].Result!.ClientIds);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.OrganizationClients.CountAsync(client => client.OrganizationId == organizationId));
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    public async Task ImportRequiresPaidFeatureAndRetainsExistingDraftAfterAccessExpires(string scenario)
    {
        var (userId, organizationId) = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var organization = await context.Organizations.FindAsync(organizationId);
        organization!.PrimaryScenarioCode = scenario;
        await context.SaveChangesAsync();
        var allowed = CreateService(context);
        var draft = await allowed.CreateAsync(userId, organizationId, "clients.csv", Encoding.UTF8.GetBytes("Имя\nАнна"), default);
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(userId, organizationId, It.IsAny<string>())).ReturnsAsync(true);
        subscriptions.Setup(service => service.HasFeatureAsync(userId, organizationId, FeatureCodes.DataImport)).ReturnsAsync(false);
        var denied = new OrganizationImportService(context, subscriptions.Object, Mock.Of<IVehicleLimitQueryService>());

        Assert.False(await denied.CanImportAsync(userId, organizationId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => denied.CreateAsync(userId, organizationId, "new.csv", Encoding.UTF8.GetBytes("Имя\nИван"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => denied.GetAsync(userId, organizationId, draft.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => denied.ListAsync(userId, organizationId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => denied.SaveAsync(userId, organizationId, draft.Id, draft.Revision, [], default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => denied.ConfirmAsync(userId, organizationId, draft.Id, draft.Revision, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => denied.GetReportAsync(userId, organizationId, draft.Id, default));
        Assert.Equal(draft.Id, (await allowed.GetAsync(userId, organizationId, draft.Id, default)).Id);
        Assert.False(await context.OrganizationClients.AnyAsync(client => client.OrganizationId == organizationId));
    }

    private async Task<(string UserId, Guid OrganizationId)> CreateOrganizationAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Импорт", OwnerUser = user };
        organization.Members.Add(new OrganizationMember { User = user, RoleCode = "owner" });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return (user.Id, organization.Id);
    }

    private static OrganizationImportService CreateService(
        OpenCRMContext context,
        int maxVehicles = 100,
        int maxClients = int.MaxValue)
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        subscriptions.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto
            {
                Features = [new FeatureValueDto { Code = FeatureCodes.WorkspaceClientsLimit, IntValue = maxClients }]
            });
        var limits = new Mock<IVehicleLimitQueryService>();
        limits.Setup(service => service.GetVehicleLimitInfoAsync(It.IsAny<string>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new VehicleLimitInfoDto { MaxVehiclesForUser = maxVehicles });

        return new OrganizationImportService(context, subscriptions.Object, limits.Object);
    }
}
