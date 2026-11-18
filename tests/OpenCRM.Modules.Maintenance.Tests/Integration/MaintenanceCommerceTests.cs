using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Inventory.Contracts;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceCommerceTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task PaymentRetryAndCancellationDoNotDuplicateMoneyOrAudit()
    {
        await using var context = fixture.CreateContext();
        var work = await MaintenanceCommerceTests.Seed(context);
        var service = MaintenanceCommerceTests.Service(context);
        var transaction = new ApplicationUnitOfWork(context);
        var dto = new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 250, Comment = "Аванс" };
        var organizationId = work.Vehicle.OrganizationId!.Value;
        await transaction.ExecuteInTransactionAsync(() => service.AddPayment(work.UserId, organizationId, work.Id, dto));
        await transaction.ExecuteInTransactionAsync(() => service.AddPayment(work.UserId, organizationId, work.Id, dto));
        context.ChangeTracker.Clear();
        Assert.Equal(250, (await context.Maintenance.FindAsync(work.Id))!.PaidAmount);
        Assert.Equal(1, await context.MaintenancePayments.CountAsync(item => item.MaintenanceId == work.Id));
        Assert.Equal(1, await context.MaintenanceHistoryEvents.CountAsync(item => item.MaintenanceId == work.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() =>
            service.CancelPayment(work.UserId, organizationId, work.Id, dto.Id, " ")));
        await transaction.ExecuteInTransactionAsync(() => service.CancelPayment(work.UserId, organizationId, work.Id, dto.Id, "Внесено ошибочно"));
        await transaction.ExecuteInTransactionAsync(() => service.CancelPayment(work.UserId, organizationId, work.Id, dto.Id, "Повтор"));
        await transaction.ExecuteInTransactionAsync(() => service.AddPayment(work.UserId, organizationId, work.Id, dto));
        context.ChangeTracker.Clear();
        var saved = (await context.Maintenance.FindAsync(work.Id))!;
        Assert.Equal(0, saved.PaidAmount);
        Assert.Equal(50, saved.MaterialCostAmount);
        Assert.Equal(20, saved.LaborCostAmount);
        var history = await service.GetPayments(work.UserId, organizationId, work.Id);
        Assert.Equal("Внесено ошибочно", Assert.Single(history).CancellationReason);
        Assert.Equal(2, await context.MaintenanceHistoryEvents.CountAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task ConcurrentPaymentsUseTheCurrentBalanceAndRejectStaleCard()
    {
        await using var context = fixture.CreateContext();
        var work = await MaintenanceCommerceTests.Seed(context);
        var id = Guid.NewGuid();
        async Task Receive(Guid requestId)
        {
            await using var attempt = fixture.CreateContext();
            await new ApplicationUnitOfWork(attempt).ExecuteInTransactionAsync(() => MaintenanceCommerceTests.Service(attempt)
                .AddPayment(work.UserId, work.Vehicle.OrganizationId!.Value, work.Id, new AddMaintenancePaymentDto { Id = requestId, Amount = 250 }));
        }
        await Task.WhenAll(Receive(id), Receive(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Receive(Guid.NewGuid()));
        context.ChangeTracker.Clear();
        Assert.Equal(250, (await context.Maintenance.FindAsync(work.Id))!.PaidAmount);
        Assert.Equal(1, await context.MaintenancePayments.CountAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task RejectsOverpaymentForeignRecordAndMissingFinancePermission()
    {
        await using var context = fixture.CreateContext();
        var work = await MaintenanceCommerceTests.Seed(context);
        var transaction = new ApplicationUnitOfWork(context);
        var service = MaintenanceCommerceTests.Service(context);
        foreach (var amount in new[] { 0m, -1m, 0.001m, 901m })
        {
            var error = await Assert.ThrowsAsync<MaintenancePaymentValidationException>(() => transaction.ExecuteInTransactionAsync(() => service.AddPayment(
                work.UserId, work.Vehicle.OrganizationId!.Value, work.Id, new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = amount })));
            Assert.Equal(amount == 901m ? "maintenance.payment.overpayment" : "maintenance.payment.amountInvalid", error.MessageKey);
            if (amount == 901m)
            {
                Assert.Equal("Сумма больше остатка к оплате: 900 RUB. Обновите карточку.", error.Message);
                Assert.Equal("900 RUB", Assert.Single(error.Arguments));
            }
            else
            {
                Assert.Equal("Укажите сумму оплаты больше нуля, не более двух знаков после запятой.", error.Message);
                Assert.Empty(error.Arguments);
            }
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() => service.AddPayment(
            work.UserId, Guid.NewGuid(), work.Id, new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 100 })));
        var permissions = new Mock<IOrganizationSubscriptionService>();
        permissions.Setup(item => item.EnsureWorkspaceCanWriteAsync(work.UserId, work.Vehicle.OrganizationId!.Value, OrganizationPermissionCodes.FinanceManage))
            .ThrowsAsync(new InvalidOperationException("Нет доступа"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() => MaintenanceCommerceTests.Service(context, permissions.Object)
            .AddPayment(work.UserId, work.Vehicle.OrganizationId!.Value, work.Id, new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 100 })));
        Assert.False(await context.MaintenancePayments.AnyAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task RejectsFinancialChangesWhenMaintenanceSnapshotUsesAnotherCurrency()
    {
        await using var context = fixture.CreateContext();
        var work = await MaintenanceCommerceTests.Seed(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        work.Vehicle.Organization!.CurrencyCode = CurrencyCodes.Cny;
        await context.SaveChangesAsync();

        var service = MaintenanceCommerceTests.Service(context);
        var transaction = new ApplicationUnitOfWork(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() => service.AddPayment(
            work.UserId,
            organizationId,
            work.Id,
            new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 100 })));

        var catalogItem = await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.OrganizationId == organizationId);
        var update = new UpdateMaintenanceServicesDto
        {
            Version = work.Version,
            Details =
            [
                new CreateMaintenanceDetailDto
                {
                    Name = catalogItem.Name,
                    CatalogItemId = catalogItem.Id,
                    Quantity = 1,
                    Price = 200
                }
            ]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() => service.UpdateServices(
            work.UserId,
            organizationId,
            work.Id,
            update)));

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance
            .Include(item => item.Details)
            .SingleAsync(item => item.Id == work.Id);
        Assert.Equal(CurrencyCodes.Rub, saved.CurrencyCode);
        Assert.Equal(0, saved.PaidAmount);
        Assert.Equal(1000, saved.Details.Single(detail => detail.Price.HasValue).Price);
        Assert.False(await context.MaintenancePayments.AnyAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task ServiceChangeKeepsMaterialsPaymentsAndHistoricalPricesAndRejectsStaleVersion()
    {
        await using var context = fixture.CreateContext();
        var work = await MaintenanceCommerceTests.Seed(context);
        var oldVersion = work.Version;
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var catalogItem = await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.OrganizationId == organizationId);
        catalogItem.UnitPrice = 99000;
        catalogItem.Name = "Новое название";
        await context.SaveChangesAsync();
        var service = MaintenanceCommerceTests.Service(context);
        var transaction = new ApplicationUnitOfWork(context);
        await transaction.ExecuteInTransactionAsync(() => service.AddPayment(work.UserId, organizationId, work.Id,
            new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 200 }));
        context.ChangeTracker.Clear();
        var saved = (await context.Maintenance.FindAsync(work.Id))!;
        var materials = saved.Details.Last();
        var request = new UpdateMaintenanceServicesDto
        {
            Version = saved.Version,
            Details = [new CreateMaintenanceDetailDto { Name = "Новое название", CatalogItemId = catalogItem.Id, Quantity = 2, Price = 2000, Description = "Два элемента" }]
        };
        await transaction.ExecuteInTransactionAsync(() => service.UpdateServices(work.UserId, organizationId, work.Id, request));
        context.ChangeTracker.Clear();
        saved = (await context.Maintenance.FindAsync(work.Id))!;
        Assert.Equal("Полировка", saved.Details[0].Name);
        Assert.Equal(2000, saved.Details[0].Price);
        Assert.Equal(2, saved.Details[0].Quantity);
        Assert.Equal(materials.MaterialUnitCost, saved.Details.Last().MaterialUnitCost);
        Assert.Equal(200, saved.PaidAmount);
        Assert.Equal(50, saved.MaterialCostAmount);
        Assert.Equal(work.Status, saved.Status);
        request.Version = oldVersion;
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() => service.UpdateServices(work.UserId, organizationId, work.Id, request)));
        Assert.Contains(await context.MaintenanceHistoryEvents.Where(item => item.MaintenanceId == work.Id).ToListAsync(), item => item.Description!.Contains("количество: 1 -> 2"));
    }

    [Fact]
    public async Task BundlesReferenceOnlyActiveServicesOfTheirOrganization()
    {
        await using var context = fixture.CreateContext();
        using var catalogServices = new CatalogTestServices(context);
        var work = await MaintenanceCommerceTests.Seed(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var serviceId = work.Details[0].CatalogItemId!.Value;
        var catalog = catalogServices.Query;
        var bundle = await catalogServices.Commands.CreateAsync(work.UserId, organizationId, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle, Name = "Подготовка к продаже", ServiceIds = [serviceId]
        });
        Assert.Equal(serviceId, Assert.Single(bundle.ServiceIds));
        foreach (var ids in new[] { Array.Empty<Guid>(), new[] { serviceId, serviceId }, new[] { Guid.NewGuid() } })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => catalogServices.Commands.CreateAsync(work.UserId, organizationId,
                new CreateOrganizationScenarioCatalogItemDto { KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle, Name = "Неверный набор", ServiceIds = ids }));
        }
        await catalogServices.Commands.DeleteAsync(work.UserId, organizationId, serviceId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalogServices.Commands.CreateAsync(work.UserId, organizationId,
            new CreateOrganizationScenarioCatalogItemDto { KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle, Name = "Архив", ServiceIds = [serviceId] }));
        Assert.Equal(1000, (await context.Maintenance.FindAsync(work.Id))!.Details[0].Price);
    }

    [Fact]
    public async Task OldEditorCannotOverwritePaymentsOrMoreRecentServices()
    {
        await using var context = fixture.CreateContext();
        var work = await MaintenanceCommerceTests.Seed(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var dto = new UpdateMaintenanceDto
        {
            Version = work.Version, Status = work.Status, Type = work.Type, PlannedDate = work.PlannedDate,
            CompletedDate = null, Location = null, Performer = null, Mileage = null, Comment = null,
            DiscountAmount = work.DiscountAmount, LaborCostAmount = work.LaborCostAmount, PaidAmount = 999999,
            Details = work.Details.Select(detail => new CreateMaintenanceDetailDto
            {
                Name = detail.Name, Description = detail.Description, Price = detail.Price, CatalogItemId = detail.CatalogItemId,
                MaterialName = detail.MaterialName, MaterialQuantity = detail.MaterialQuantity, MaterialUnitCost = detail.MaterialUnitCost
            }).ToList()
        };
        var service = MaintenanceCommerceTests.Service(context);
        var transaction = new ApplicationUnitOfWork(context);
        await transaction.ExecuteInTransactionAsync(() => service.AddPayment(work.UserId, organizationId, work.Id,
            new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 200 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() => service.Update(work.UserId, organizationId, work.Id, dto)));
        dto.Version = null;
        await transaction.ExecuteInTransactionAsync(() => service.Update(work.UserId, organizationId, work.Id, dto));
        context.ChangeTracker.Clear();
        Assert.Equal(200, (await context.Maintenance.FindAsync(work.Id))!.PaidAmount);
        Assert.Equal(1, await context.MaintenancePayments.CountAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task MigrationPreservesLegacyBalanceWithoutInventingPersonalPayments()
    {
        await using var context = fixture.CreateContext();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var work = await MaintenanceCommerceTests.Seed(context);
            work.PaidAmount = 275;
            var personal = new MaintenanceEntity
            {
                User = work.User, Vehicle = new Vehicle { User = work.User }, PaidAmount = 125,
                Date = DateTime.UtcNow.Date, CompletedDate = DateTime.UtcNow.Date, Details = []
            };
            context.Maintenance.Add(personal);
            await context.SaveChangesAsync();
            var migration = new OpenCRM.Infrastructure.Migrations.Migrations.AddMaintenancePaymentsAndServiceBundles();
            var sql = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Single().Sql;
            await context.Database.ExecuteSqlRawAsync(sql);
            var payment = await context.MaintenancePayments.SingleAsync(item => item.MaintenanceId == work.Id);
            Assert.True(payment.IsOpeningBalance);
            Assert.Equal(275, payment.Amount);
            Assert.False(await context.MaintenancePayments.AnyAsync(item => item.MaintenanceId == personal.Id));
            await context.Entry(work).ReloadAsync();
            Assert.Equal(275, work.PaidAmount);
            await transaction.RollbackAsync();
        });
    }

    private static MaintenanceTestScope Service(OpenCRMContext context, IOrganizationSubscriptionService? permissions = null)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(item => item.UtcNow).Returns(DateTime.UtcNow);
        var consumption = new Mock<IMaintenanceConsumptionService>();
        consumption.Setup(item => item.SynchronizeAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
            .ReturnsAsync(new MaintenanceConsumptionResult(0, []));

        return new MaintenanceTestScope(new MaintenanceRepository(context),
            Mock.Of<IVehicleCounterCommandService>(), clock.Object, context,
            permissions ?? Mock.Of<IOrganizationSubscriptionService>(), Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(), consumption.Object);
    }

    private static async Task<MaintenanceEntity> Seed(OpenCRMContext context)
    {
        var user = new ApplicationUser { Email = "commerce@example.test" };
        var organization = new Organization { Name = "Проверка расчётов", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var catalog = new OrganizationScenarioCatalogItem { Organization = organization, Name = "Полировка", KindCode = "service", Code = "polishing", ScenarioCode = "detailing", UnitPrice = 1000 };
        var work = new MaintenanceEntity
        {
            User = user, Vehicle = vehicle, Status = MaintenanceStatus.Planned, Date = DateTime.UtcNow.Date, PlannedDate = DateTime.UtcNow.Date,
            DiscountAmount = 100, MaterialCostAmount = 50, LaborCostAmount = 20,
            Details = [new MaintenanceDetail { Name = "Полировка", CatalogItemId = catalog.Id, Price = 1000 },
                new MaintenanceDetail { Name = "Материал", MaterialName = "Состав", MaterialQuantity = 1, MaterialUnitCost = 50, MaterialTotalCost = 50 }]
        };
        context.OrganizationScenarioCatalogItems.Add(catalog);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();

        return work;
    }
}
