using Helpers;
using System.Data.Common;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Testing;
using OpenCRM.Modules.Vehicles.Counters;
using OpenCRM.Modules.Workspace.Workday;
using OpenCRM.Modules.Workspace.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Npgsql;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Organizations;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Inventory.Contracts;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceServiceCatalogTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task CreatedMaterialDetailPersistsSynchronizedInventoryCost()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var consumption = new Mock<IMaintenanceConsumptionService>();
        consumption.Setup(service => service.SynchronizeAsync(
                vehicle.User.Id,
                vehicle.Organization!.Id,
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
            .ReturnsAsync((string _, Guid _, Guid _, IReadOnlyCollection<MaintenanceMaterialConsumptionLine> details) =>
                new MaintenanceConsumptionResult(
                    25,
                    details.Where(detail => detail.Quantity.HasValue)
                        .Select(detail => new MaintenanceMaterialConsumptionCost(detail.DetailId, 12.5m, 25m)).ToArray()));
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle, consumptionService: consumption.Object);
        var request = MaintenanceServiceCatalogTests.CreateRequest(null);
        request.Details =
        [
            new CreateMaintenanceDetailDto { Name = "Работа", Price = 1000 },
            new CreateMaintenanceDetailDto { Name = "Материал", MaterialName = "Состав", MaterialQuantity = 2, MaterialUnit = "мл" }
        ];

        var maintenanceId = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var maintenance = await context.Maintenance.SingleAsync(item => item.Id == maintenanceId);
        var detail = Assert.Single(maintenance.Details, detail => detail.MaterialQuantity.HasValue);

        Assert.Equal(12.5m, detail.MaterialUnitCost);
        Assert.Equal(25m, detail.MaterialTotalCost);
        Assert.Equal(25m, maintenance.MaterialCostAmount);
    }

    [Fact]
    public async Task CatalogDurationRoundTripsWithoutChangingExistingSchedule()
    {
        await using var context = fixture.CreateContext();
        using var catalogServices = new CatalogTestServices(context);
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var organization = vehicle.Organization!;
        var catalog = catalogServices.Query;
        var item = await catalogServices.Commands.CreateAsync(vehicle.User.Id, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service, Name = "Полировка с сушкой", DurationMinutes = 1500
        });
        var bundle = await catalogServices.Commands.CreateAsync(vehicle.User.Id, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle, Name = "Комплекс", ServiceIds = [item.Id], DurationMinutes = 120
        });
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.PlannedStartTime = TimeSpan.FromHours(9);
        request.PlannedEndTime = TimeSpan.FromHours(11);
        var maintenanceId = await MaintenanceServiceCatalogTests.CreateService(context, vehicle).Create(vehicle.User.Id, organization.Id, vehicle.Id, request);
        await catalogServices.Commands.UpdateAsync(vehicle.User.Id, organization.Id, item.Id, new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = item.Name, IsActive = true, DurationMinutes = 60
        });
        context.ChangeTracker.Clear();
        var loaded = await catalog.GetAsync(vehicle.User.Id, organization.Id);
        Assert.Equal(60, loaded.Services.Single(value => value.Id == item.Id).DurationMinutes);
        Assert.Equal(120, loaded.ServiceBundles.Single(value => value.Id == bundle.Id).DurationMinutes);
        var maintenance = await context.Maintenance.SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal(TimeSpan.FromHours(11), maintenance.PlannedEndTime);
        await catalogServices.Commands.UpdateAsync(vehicle.User.Id, organization.Id, item.Id, new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = item.Name, IsActive = true, DurationMinutes = null
        });
        Assert.Null((await catalog.GetActiveServiceAsync(vehicle.User.Id, organization.Id, item.Id)).DurationMinutes);
        foreach (var duration in new[] { -1, 0, 525601 })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => catalogServices.Commands.CreateAsync(vehicle.User.Id, organization.Id, new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.Service, Name = "Некорректная длительность", DurationMinutes = duration
            }));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalogServices.Commands.CreateAsync(vehicle.User.Id, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.WorkPost, Name = "Пост с длительностью", DurationMinutes = 60
        }));
    }

    [Fact]
    public async Task CustomWorksRoundTripAlongsideCatalogWithoutPollutingIt()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Details = request.Details.Concat([
            new CreateMaintenanceDetailDto { Name = "  Удаление клея  ", Price = 500, Quantity = 2, Description = null },
            new CreateMaintenanceDetailDto { Name = "Осмотр", Price = 0, Description = null }
        ]).ToList();
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Equal(new[] { "Полировка", "Удаление клея", "Осмотр" }, work.Details.Select(detail => detail.Name));
        Assert.Null(work.Details[1].CatalogItemId);
        Assert.Null(work.Details[2].CatalogItemId);
        Assert.Equal(2, work.Details[1].Quantity);

        var update = new UpdateMaintenanceDto
        {
            Status = work.Status, Type = work.Type, PlannedDate = work.PlannedDate, CompletedDate = null,
            Location = null, Performer = null, Mileage = null, Comment = null,
            Details = work.Details.Select(detail => new CreateMaintenanceDetailDto
            {
                CatalogItemId = detail.CatalogItemId, Name = detail.CatalogItemId.HasValue ? detail.Name : detail.Name + " вручную",
                Price = detail.Price, Quantity = detail.Quantity, Description = detail.Description
            }).ToList()
        };
        await service.Update(vehicle.User.Id, vehicle.Organization.Id, id, update);
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Equal(new[] { "Полировка", "Удаление клея вручную", "Осмотр вручную" }, saved.Details.Select(detail => detail.Name));
        Assert.Equal(1, await context.OrganizationScenarioCatalogItems.CountAsync(value => value.OrganizationId == vehicle.Organization.Id));

        var invalid = MaintenanceServiceCatalogTests.CreateRequest(null);
        invalid.Details.Single().Name = "   ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, invalid));
        Assert.Equal(1, await context.Maintenance.CountAsync(value => value.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task RejectsMissingForeignInactiveAndStageIdsBeforeWriting()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var foreign = MaintenanceServiceCatalogTests.CreateItem(MaintenanceServiceCatalogTests.CreateVehicle().Organization!, "Чужая услуга");
        var stage = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Приемка");
        stage.KindCode = OrganizationScenarioCatalogKindCodes.WorkType;
        var archived = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Архивная");
        archived.IsActive = false;
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(foreign, stage, archived);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);

        foreach (var id in new Guid?[] { Guid.Empty, Guid.NewGuid(), foreign.Id, stage.Id, archived.Id })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
                vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, MaintenanceServiceCatalogTests.CreateRequest(id)));
        }

        Assert.False(await context.Maintenance.AnyAsync(item => item.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task NewMaintenanceRejectsArchivedCatalogMaterialEvenWithoutQuantity()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var material = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Архивный материал");
        material.KindCode = OrganizationScenarioCatalogKindCodes.Material;
        material.IsActive = false;
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(material);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(null);
        request.Details = request.Details.Append(new CreateMaintenanceDetailDto
        {
            Name = material.Name,
            MaterialCatalogItemId = material.Id,
            MaterialName = material.Name,
            MaterialQuantity = 0
        }).ToList();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
            vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request));

        Assert.Contains("Материал не найден в справочнике", exception.Message);
        Assert.False(await context.Maintenance.AnyAsync(item => item.VehicleId == vehicle.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public async Task NewMaintenancePersistsActiveCatalogMaterialWithoutPositiveQuantity(int? quantity)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var material = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Активный материал");
        material.KindCode = OrganizationScenarioCatalogKindCodes.Material;
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(material);
        await context.SaveChangesAsync();
        var request = MaintenanceServiceCatalogTests.CreateRequest(null);
        request.Details = request.Details.Append(new CreateMaintenanceDetailDto
        {
            Name = material.Name,
            MaterialCatalogItemId = material.Id,
            MaterialName = material.Name,
            MaterialQuantity = quantity,
            MaterialUnit = "л"
        }).ToList();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);

        var maintenanceId = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);

        var saved = await context.Maintenance.SingleAsync(item => item.Id == maintenanceId);
        var detail = Assert.Single(saved.Details, item => item.MaterialCatalogItemId == material.Id);
        Assert.Equal(material.Name, detail.MaterialName);
        Assert.Equal(quantity, detail.MaterialQuantity);
        Assert.Equal("л", detail.MaterialUnit);
    }

    [Fact]
    public async Task UpdatingMaintenanceAddsNewActiveCatalogMaterialAndKeepsExistingWorkDetail()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var material = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Новый материал");
        material.KindCode = OrganizationScenarioCatalogKindCodes.Material;
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(material);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var maintenanceId = await service.Create(
            vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, MaintenanceServiceCatalogTests.CreateRequest(null));
        var maintenance = await context.Maintenance.SingleAsync(item => item.Id == maintenanceId);
        var workDetail = Assert.Single(maintenance.Details);

        await service.Update(vehicle.User.Id, vehicle.Organization.Id, maintenanceId, new UpdateMaintenanceDto
        {
            Status = maintenance.Status,
            Type = maintenance.Type,
            PlannedDate = maintenance.PlannedDate,
            CompletedDate = maintenance.CompletedDate,
            Location = maintenance.Location,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            Comment = maintenance.Comment,
            Details =
            [
                new CreateMaintenanceDetailDto
                {
                    DetailId = workDetail.DetailId,
                    Name = workDetail.Name,
                    Price = workDetail.Price
                },
                new CreateMaintenanceDetailDto
                {
                    Name = material.Name,
                    MaterialCatalogItemId = material.Id,
                    MaterialName = material.Name,
                    MaterialQuantity = 1,
                    MaterialUnit = "л"
                }
            ]
        });

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(item => item.Id == maintenanceId);
        var savedWork = Assert.Single(saved.Details, detail => detail.Name == workDetail.Name);
        var savedMaterial = Assert.Single(saved.Details, detail => detail.MaterialCatalogItemId == material.Id);
        Assert.Equal(workDetail.DetailId, savedWork.DetailId);
        Assert.NotEqual(Guid.Empty, savedMaterial.DetailId);
        Assert.NotEqual(savedWork.DetailId, savedMaterial.DetailId);
        Assert.Equal(maintenance.PlannedDate, saved.PlannedDate);
    }

    [Fact]
    public async Task NewMaintenanceRejectsMaterialFromAnotherOrganization()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var foreignVehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var material = MaintenanceServiceCatalogTests.CreateItem(foreignVehicle.Organization!, "Чужой материал");
        material.KindCode = OrganizationScenarioCatalogKindCodes.Material;
        context.Vehicles.AddRange(vehicle, foreignVehicle);
        context.OrganizationScenarioCatalogItems.Add(material);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(null);
        request.Details = request.Details.Append(new CreateMaintenanceDetailDto
        {
            Name = material.Name,
            MaterialCatalogItemId = material.Id,
            MaterialName = material.Name,
            MaterialQuantity = 1,
            MaterialUnit = "л"
        }).ToList();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
            vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request));

        Assert.False(await context.Maintenance.AnyAsync(item => item.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task ExistingHistoricalMaterialRemainsEditableAfterCatalogMaterialIsArchived()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var material = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Архивный исторический материал");
        material.KindCode = OrganizationScenarioCatalogKindCodes.Material;
        material.IsActive = false;
        var workDetailId = Guid.NewGuid();
        var materialDetailId = Guid.NewGuid();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            User = vehicle.User,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned,
            PlannedDate = DateTime.UtcNow.Date,
            Type = MaintenanceType.Maintenance,
            Details =
            [
                new MaintenanceDetail { DetailId = workDetailId, Position = 0, Name = "Работа", Price = 100 },
                new MaintenanceDetail
                {
                    DetailId = materialDetailId,
                    Position = 1,
                    Name = material.Name,
                    MaterialCatalogItemId = material.Id,
                    MaterialName = material.Name,
                    MaterialQuantity = 2,
                    MaterialUnit = "л"
                }
            ]
        };
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(material);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);

        await service.Update(vehicle.User.Id, vehicle.Organization!.Id, maintenance.Id, new UpdateMaintenanceDto
        {
            Status = maintenance.Status,
            Type = maintenance.Type,
            PlannedDate = maintenance.PlannedDate,
            CompletedDate = maintenance.CompletedDate,
            Location = maintenance.Location,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            Comment = "Обновлен комментарий",
            Details =
            [
                new CreateMaintenanceDetailDto { DetailId = workDetailId, Name = "Работа", Price = 100 },
                new CreateMaintenanceDetailDto
                {
                    DetailId = materialDetailId,
                    Name = material.Name,
                    MaterialCatalogItemId = material.Id,
                    MaterialName = material.Name,
                    MaterialQuantity = 2,
                    MaterialUnit = "л"
                }
            ]
        });

        var saved = await context.Maintenance.SingleAsync(item => item.Id == maintenance.Id);
        Assert.Equal("Обновлен комментарий", saved.Comment);
        Assert.Equal(material.Id, saved.Details.Single(detail => detail.DetailId == materialDetailId).MaterialCatalogItemId);
    }

    [Fact]
    public async Task ChangingHistoricalMaterialToArchivedMaterialIsRejected()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var activeMaterial = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Действующий материал");
        activeMaterial.KindCode = OrganizationScenarioCatalogKindCodes.Material;
        var archivedMaterial = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Материал в архиве");
        archivedMaterial.KindCode = OrganizationScenarioCatalogKindCodes.Material;
        archivedMaterial.IsActive = false;
        var workDetailId = Guid.NewGuid();
        var materialDetailId = Guid.NewGuid();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            User = vehicle.User,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned,
            PlannedDate = DateTime.UtcNow.Date,
            Type = MaintenanceType.Maintenance,
            Details =
            [
                new MaintenanceDetail { DetailId = workDetailId, Position = 0, Name = "Работа", Price = 100 },
                new MaintenanceDetail
                {
                    DetailId = materialDetailId,
                    Position = 1,
                    Name = activeMaterial.Name,
                    MaterialCatalogItemId = activeMaterial.Id,
                    MaterialName = activeMaterial.Name,
                    MaterialQuantity = 2,
                    MaterialUnit = "л"
                }
            ]
        };
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(activeMaterial, archivedMaterial);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = new UpdateMaintenanceDto
        {
            Status = maintenance.Status,
            Type = maintenance.Type,
            PlannedDate = maintenance.PlannedDate,
            CompletedDate = maintenance.CompletedDate,
            Location = maintenance.Location,
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            Comment = "Изменение должно быть отклонено",
            Details =
            [
                new CreateMaintenanceDetailDto { DetailId = workDetailId, Name = "Работа", Price = 100 },
                new CreateMaintenanceDetailDto
                {
                    DetailId = materialDetailId,
                    Name = archivedMaterial.Name,
                    MaterialCatalogItemId = archivedMaterial.Id,
                    MaterialName = archivedMaterial.Name,
                    MaterialQuantity = 2,
                    MaterialUnit = "л"
                }
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(
            vehicle.User.Id, vehicle.Organization!.Id, maintenance.Id, request));

        Assert.Null(maintenance.Comment);
        Assert.Equal(activeMaterial.Id, maintenance.Details.Single(detail => detail.DetailId == materialDetailId).MaterialCatalogItemId);
    }

    [Fact]
    public async Task NewMaintenanceCannotAssignAnArchivedPost()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var archivedPost = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Архивный пост");
        archivedPost.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        archivedPost.IsActive = false;
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(archivedPost);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(null);
        request.Location = archivedPost.Name;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
            vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request));

        Assert.False(await context.Maintenance.AnyAsync(item => item.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task UpdateKeepsPreviousArchivedPostButRejectsChangingToAnotherArchivedPost()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var previousPost = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Пост 8");
        previousPost.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        previousPost.IsActive = false;
        var nextPost = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Пост 9");
        nextPost.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        nextPost.IsActive = false;
        var workDetail = new MaintenanceDetail { Name = "Работа", Price = 100 };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            User = vehicle.User,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned,
            PlannedDate = DateTime.UtcNow.Date,
            Type = MaintenanceType.Maintenance,
            Location = "  ПОСТ 8  ",
            Details = [workDetail]
        };
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(previousPost, nextPost);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var update = new UpdateMaintenanceDto
        {
            Status = maintenance.Status,
            Type = maintenance.Type,
            PlannedDate = maintenance.PlannedDate,
            CompletedDate = maintenance.CompletedDate,
            Location = " пост 8 ",
            Performer = maintenance.Performer,
            Mileage = maintenance.Mileage,
            Comment = "Сохраняем исторический пост",
            Details = [new CreateMaintenanceDetailDto { DetailId = workDetail.DetailId, Name = workDetail.Name, Price = workDetail.Price }]
        };

        await service.Update(vehicle.User.Id, vehicle.Organization!.Id, maintenance.Id, update);
        update.Location = nextPost.Name;
        update.Comment = "Новый архивный пост запрещен";

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(
            vehicle.User.Id, vehicle.Organization.Id, maintenance.Id, update));

        Assert.Equal("Сохраняем исторический пост", maintenance.Comment);
        Assert.Equal("пост 8", maintenance.Location);
    }

    [Theory]
    [InlineData("edit-date")]
    [InlineData("edit-time")]
    [InlineData("edit-reopen")]
    [InlineData("planned-date")]
    [InlineData("reschedule")]
    [InlineData("reopen")]
    [InlineData("return_to_work")]
    public async Task ClosedPostRejectsNewReservationThroughEveryUpdateRouteWithoutPartialSave(string action)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var post = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "QA Закрытый пост");
        post.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        post.IsActive = false;
        var detail = new MaintenanceDetail { Name = "Сохранённая работа", Quantity = 1, Price = 100 };
        var date = DateTime.UtcNow.Date.AddDays(1);
        var originalStatus = action is "reopen" or "edit-reopen" ? MaintenanceStatus.Cancelled
            : action == "return_to_work" ? MaintenanceStatus.Completed : MaintenanceStatus.Planned;
        var work = new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Date = date, DateTimeCreation = DateTime.UtcNow,
            Type = MaintenanceType.Maintenance, Status = originalStatus,
            PlannedDate = date, PlannedEndDate = date, PlannedStartTime = TimeSpan.FromHours(10), PlannedEndTime = TimeSpan.FromHours(11),
            CompletedDate = originalStatus == MaintenanceStatus.Completed ? date : null,
            Location = post.Name, Comment = "Исходная запись", Details = [detail]
        };
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(post);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var version = work.Version;
        using var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var transaction = new ApplicationUnitOfWork(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(async () =>
        {
            if (action is "edit-date" or "edit-time" or "edit-reopen")
            {
                await service.Update(vehicle.User.Id, vehicle.Organization!.Id, work.Id, new UpdateMaintenanceDto
                {
                    Status = action == "edit-reopen" ? MaintenanceStatus.Planned : originalStatus,
                    Type = work.Type,
                    PlannedDate = action == "edit-date" ? date.AddDays(1) : date,
                    PlannedEndDate = action == "edit-date" ? date.AddDays(1) : date,
                    PlannedStartTime = action == "edit-time" ? TimeSpan.FromHours(12) : work.PlannedStartTime,
                    PlannedEndTime = action == "edit-time" ? TimeSpan.FromHours(13) : work.PlannedEndTime,
                    Location = post.Name, Performer = work.Performer, Mileage = work.Mileage, CompletedDate = work.CompletedDate, Comment = "Не должно сохраниться",
                    Details = [new CreateMaintenanceDetailDto { DetailId = detail.DetailId, Name = detail.Name, Price = 100, Quantity = 1 }]
                });

                return;
            }

            if (action == "planned-date")
            {
                await service.UpdatePlannedDate(vehicle.User.Id, vehicle.Organization!.Id, work.Id,
                    new UpdateMaintenancePlannedDateDto { PlannedDate = date.AddDays(1), PlannedEndDate = date.AddDays(1) });

                return;
            }

            await service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization!.Id, work.Id, new MaintenanceWorkflowActionDto
            {
                Action = action, Version = version, Location = post.Name,
                PlannedDate = date.AddDays(1), PlannedEndDate = date.AddDays(1),
                PlannedStartTime = TimeSpan.FromHours(12), PlannedEndTime = TimeSpan.FromHours(13)
            });
        }));

        Assert.Contains("архив", error.Message, StringComparison.OrdinalIgnoreCase);
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(item => item.Id == work.Id);
        Assert.Equal(date, saved.PlannedDate);
        Assert.Equal(TimeSpan.FromHours(10), saved.PlannedStartTime);
        Assert.Equal(originalStatus, saved.Status);
        Assert.Equal("Исходная запись", saved.Comment);
        Assert.Equal(version, saved.Version);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(item => item.MaintenanceId == work.Id).ToListAsync());
    }

    [Theory]
    [InlineData("comment")]
    [InlineData("complete")]
    [InlineData("start")]
    [InlineData("move-active")]
    public async Task ClosedPostKeepsExistingWorkEditableAndCanMoveItToAnActivePost(string action)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var closed = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "QA Закрыт");
        closed.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        closed.IsActive = false;
        var active = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "QA Открыт");
        active.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        var detail = new MaintenanceDetail { Name = "Работа", Price = 100, Quantity = 1 };
        var date = DateTime.UtcNow.Date.AddDays(1);
        var work = new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Date = date, DateTimeCreation = DateTime.UtcNow,
            Type = MaintenanceType.Maintenance, Status = MaintenanceStatus.Planned,
            PlannedDate = date, PlannedEndDate = date, PlannedStartTime = TimeSpan.FromHours(10), PlannedEndTime = TimeSpan.FromHours(11),
            Location = closed.Name, Details = [detail]
        };
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(closed, active);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var transaction = new ApplicationUnitOfWork(context);

        await transaction.ExecuteInTransactionAsync(async () =>
        {
            if (action == "comment")
            {
                await service.Update(vehicle.User.Id, vehicle.Organization!.Id, work.Id, new UpdateMaintenanceDto
                {
                    Status = work.Status, Type = work.Type, PlannedDate = date, PlannedEndDate = date,
                    PlannedStartTime = work.PlannedStartTime, PlannedEndTime = work.PlannedEndTime,
                    Location = closed.Name, Performer = work.Performer, Mileage = work.Mileage, CompletedDate = work.CompletedDate, Comment = "Исправлен комментарий",
                    Details = [new CreateMaintenanceDetailDto { DetailId = detail.DetailId, Name = detail.Name, Price = 200, Quantity = 1 }]
                });

                return;
            }

            await service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization!.Id, work.Id, new MaintenanceWorkflowActionDto
            {
                Action = action == "move-active" ? "reschedule" : action,
                Version = work.Version, Location = action == "move-active" ? active.Name : closed.Name,
                PlannedDate = date.AddDays(1), PlannedEndDate = date.AddDays(1),
                PlannedStartTime = work.PlannedStartTime, PlannedEndTime = work.PlannedEndTime
            });
        });

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.Include(item => item.Details).SingleAsync(item => item.Id == work.Id);
        Assert.Equal(action == "move-active" ? active.Name : closed.Name, saved.Location);
        Assert.Equal(action == "move-active" ? date.AddDays(1) : date, saved.PlannedDate);
        if (action == "comment")
        {
            Assert.Equal(200m, Assert.Single(saved.Details).Price);
        }

        if (action == "complete")
        {
            Assert.Equal(MaintenanceStatus.Completed, saved.Status);
        }

        if (action == "start")
        {
            Assert.Equal(MaintenanceStatus.InProgress, saved.Status);
        }
    }

    [Fact]
    public async Task NewMaintenanceMayUsePostWhenAnActiveCatalogPostHasTheSameNameAsAnArchivedPost()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var archivedPost = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Пост 10");
        archivedPost.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        archivedPost.IsActive = false;
        var activePost = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Пост 10");
        activePost.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(archivedPost, activePost);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(null);
        request.Location = activePost.Name;

        var maintenanceId = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);

        Assert.Equal(activePost.Name, (await context.Maintenance.SingleAsync(item => item.Id == maintenanceId)).Location);
    }

    [Fact]
    public async Task ConcurrentServiceArchiveCompletesBeforeNewMaintenanceAndTheOrderIsRejected()
    {
        await using var seedContext = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var serviceItem = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка из архива");
        seedContext.Vehicles.Add(vehicle);
        seedContext.OrganizationScenarioCatalogItems.Add(serviceItem);
        await seedContext.SaveChangesAsync();

        var archiveBarrier = new ArchiveOrganizationLockBarrier();
        var orderLockSignal = new OrganizationLockAttemptSignal();
        await using var archiveContext = fixture.CreateContext(archiveBarrier);
        await using var orderContext = fixture.CreateContext(orderLockSignal);
        await using var monitorContext = fixture.CreateContext();
        await orderContext.Database.OpenConnectionAsync();
        var processId = ((NpgsqlConnection)orderContext.Database.GetDbConnection()).ProcessID;
        var orderVehicle = await orderContext.Vehicles
            .Include(item => item.User)
            .Include(item => item.Organization)
            .SingleAsync(item => item.Id == vehicle.Id);
        using var catalogServices = new CatalogTestServices(archiveContext);
        using var maintenanceService = MaintenanceServiceCatalogTests.CreateService(orderContext, orderVehicle);
        var archiveTask = catalogServices.Commands.DeleteAsync(vehicle.User.Id, vehicle.Organization!.Id, serviceItem.Id);
        Task<Guid>? createTask = null;

        try
        {
            await archiveBarrier.WaitUntilOrganizationLockIsHeldAsync();
            createTask = maintenanceService.Create(vehicle.User.Id, vehicle.Organization.Id, orderVehicle.Id,
                MaintenanceServiceCatalogTests.CreateRequest(serviceItem.Id));
            await orderLockSignal.WaitUntilOrganizationLockIsRequestedAsync();
            await MaintenanceServiceCatalogTests.WaitUntilBlockedByAnotherBackendAsync(monitorContext, processId);
        }
        finally
        {
            archiveBarrier.ReleaseArchive();
        }

        await archiveTask;
        Assert.NotNull(createTask);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => createTask!);

        Assert.Contains("в архиве", exception.Message);
        Assert.True(await seedContext.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == serviceItem.Id && !item.IsActive));
        Assert.False(await seedContext.Maintenance.AnyAsync(item => item.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task PreservesMultipleServicesHistoricalNameAndPriceAfterRenameAndArchive()
    {
        await using var context = fixture.CreateContext();
        using var catalogServices = new CatalogTestServices(context);
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        var second = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Мойка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(item, second);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Details = request.Details.Append(new CreateMaintenanceDetailDto
        {
            CatalogItemId = second.Id, Name = "Подменённое название", Price = 400, Description = null
        });
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Equal(new[] { "Полировка", "Мойка" }, work.Details.Select(detail => detail.Name));
        Assert.Equal(new decimal?[] { 1000, 400 }, work.Details.Select(detail => detail.Price));
        var catalog = catalogServices.Query;
        await catalogServices.Commands.UpdateAsync(vehicle.User.Id, vehicle.Organization.Id, item.Id, new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = "Восстановительная полировка", IsActive = true, SortOrder = 10
        });
        await catalogServices.Commands.DeleteAsync(vehicle.User.Id, vehicle.Organization.Id, item.Id);
        Assert.True(await context.OrganizationScenarioCatalogItems.AnyAsync(value => value.Id == item.Id && !value.IsActive));
        Assert.Equal("Полировка", work.Details[0].Name);

        var update = new UpdateMaintenanceDto
        {
            Status = work.Status, Type = work.Type, PlannedDate = work.PlannedDate, CompletedDate = null,
            Location = null, Performer = null, Mileage = null, Comment = "Изменён комментарий",
            Details = work.Details.Select(detail => new CreateMaintenanceDetailDto
            {
                CatalogItemId = detail.CatalogItemId, Name = "Нельзя переписать историю", Price = detail.Price, Description = detail.Description
            }).ToList()
        };
        await service.Update(vehicle.User.Id, vehicle.Organization.Id, work.Id, update);
        Assert.Equal(new[] { "Полировка", "Мойка" }, work.Details.Select(detail => detail.Name));
        Assert.Equal(new decimal?[] { 1000, 400 }, work.Details.Select(detail => detail.Price));
        update.Details = update.Details.Append(new CreateMaintenanceDetailDto
        {
            CatalogItemId = item.Id, Name = "Полировка", Price = 1000, Description = null
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(vehicle.User.Id, vehicle.Organization.Id, work.Id, update));
        Assert.Equal(2, work.Details.Count);
    }

    [Fact]
    public async Task ChangingOnlyServiceIdIsSavedAndRecordedInHistory()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var first = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Мойка");
        var second = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Мойка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(first, second);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, MaintenanceServiceCatalogTests.CreateRequest(first.Id));
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        await service.Update(vehicle.User.Id, vehicle.Organization.Id, work.Id, new UpdateMaintenanceDto
        {
            Status = work.Status, Type = work.Type, PlannedDate = work.PlannedDate, CompletedDate = null,
            Location = null, Performer = null, Mileage = null, Comment = null,
            Details = [new CreateMaintenanceDetailDto { CatalogItemId = second.Id, Name = "Мойка", Price = 1000, Description = null }]
        });
        Assert.Equal(second.Id, work.Details[0].CatalogItemId);
        Assert.True(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == id && value.EventType == "details_changed"));
    }

    [Fact]
    public async Task QuickBookingRejectsInvalidServiceBeforeCreatingVehicle()
    {
        await using var context = fixture.CreateContext();
        using var catalogServices = new CatalogTestServices(context);
        var vehicleService = new Mock<IVehicleCommandService>(MockBehavior.Strict);
        var organizationService = new Mock<IOrganizationQueryService>();
        var id = Guid.NewGuid();
        organizationService.Setup(value => value.GetByIdAsync("user", id)).ReturnsAsync(new OrganizationDto
        {
            Id = id, PrimaryScenarioCode = OrganizationTypeCodes.Detailing
        });
        var quick = new OrganizationDetailingQuickBookingService(
            context,
            Mock.Of<IQuickBookingClientLookupService>(),
            new QuickBookingVehicleLookupService(context, Mock.Of<IOrganizationSubscriptionService>()), organizationService.Object,
            Mock.Of<IOrganizationSubscriptionService>(), Mock.Of<IOrganizationClientCommandService>(), vehicleService.Object, null!,
            catalogServices.Query);
        await Assert.ThrowsAsync<InvalidOperationException>(() => quick.CreateAsync("user", id, new CreateOrganizationDetailingQuickBookingDto
        {
            ServiceId = Guid.NewGuid(), RegistrationPlate = "А123АА154", PlannedDate = DateTime.UtcNow
        }));
        vehicleService.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("detailing", false, false)]
    [InlineData("service-station", false, false)]
    [InlineData("service-station", true, false)]
    [InlineData("detailing", true, false)]
    [InlineData("detailing", false, true)]
    [InlineData("service-station", false, true)]
    [InlineData("service-station", true, true)]
    [InlineData("detailing", true, true)]
    public async Task QuickBookingPassesCatalogIdentityAndKeepsWorkflow(string scenario, bool simple, bool custom)
    {
        await using var context = fixture.CreateContext();
        using var catalogServices = new CatalogTestServices(context);
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();
        var organizationId = vehicle.Organization!.Id;
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(value => value.GetByIdAsync(vehicle.User.Id, organizationId)).ReturnsAsync(new OrganizationDto
        {
            Id = organizationId, PrimaryScenarioCode = scenario, MaintenanceWorkflowMode = simple ? "simple" : "staged"
        });
        CreateMaintenanceDto? captured = null;
        var maintenance = new Mock<OpenCRM.Modules.Maintenance.Contracts.IMaintenanceCreateService>();
        maintenance.Setup(value => value.Create(vehicle.User.Id, organizationId, vehicle.Id, It.IsAny<CreateMaintenanceDto>()))
            .Callback<string, Guid?, Guid, CreateMaintenanceDto>((_, _, _, request) => captured = request)
            .ReturnsAsync(Guid.NewGuid());
        var vehicles = new Mock<IVehicleCommandService>(MockBehavior.Strict);
        if (scenario == "detailing")
        {
            var quick = new OrganizationDetailingQuickBookingService(
                context,
                Mock.Of<IQuickBookingClientLookupService>(),
                new QuickBookingVehicleLookupService(context, Mock.Of<IOrganizationSubscriptionService>()), organizations.Object,
                Mock.Of<IOrganizationSubscriptionService>(), Mock.Of<IOrganizationClientCommandService>(), vehicles.Object, maintenance.Object,
                catalogServices.Query);
            await quick.CreateAsync(vehicle.User.Id, organizationId, new CreateOrganizationDetailingQuickBookingDto
            {
                IsPlannedBooking = false,
                ServiceId = item.Id, RegistrationPlate = vehicle.RegistrationPlate!, PlannedDate = DateTime.UtcNow, Price = 3500,
                Services = custom ? [new CreateMaintenanceDetailDto { Name = "  Разовая работа  ", Price = 1700, Description = null }] : null
            });
        }
        else
        {
            var quick = new OrganizationAutoServiceQuickBookingService(
                Mock.Of<IQuickBookingClientLookupService>(),
                new QuickBookingVehicleLookupService(context, Mock.Of<IOrganizationSubscriptionService>()), organizations.Object,
                Mock.Of<IOrganizationSubscriptionService>(), Mock.Of<IOrganizationClientCommandService>(), vehicles.Object, maintenance.Object,
                catalogServices.Query);
            await quick.CreateAsync(vehicle.User.Id, organizationId, new CreateOrganizationAutoServiceQuickBookingDto
            {
                IsPlannedBooking = false,
                ServiceId = item.Id, RegistrationPlate = vehicle.RegistrationPlate!, PlannedDate = DateTime.UtcNow, Price = 3500,
                Services = custom ? [new CreateMaintenanceDetailDto { Name = "  Разовая работа  ", Price = 1700, Description = null }] : null
            });
        }

        Assert.NotNull(captured);
        var detail = Assert.Single(captured.Details);
        Assert.Equal(custom ? (Guid?)null : item.Id, detail.CatalogItemId);
        Assert.Equal(custom ? "Разовая работа" : item.Name, detail.Name);
        Assert.Equal(custom ? 1700 : 3500, detail.Price);
        Assert.Equal(simple ? MaintenanceStatus.Completed : MaintenanceStatus.Planned, captured.Status);
        vehicles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RepairScriptLinksOnlyUniqueServicesAndPreservesHistory()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var unique = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        var archived = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Архивная мойка");
        archived.IsActive = false;
        var duplicate = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Комплекс");
        var duplicateAgain = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Комплекс");
        var foreign = MaintenanceServiceCatalogTests.CreateItem(MaintenanceServiceCatalogTests.CreateVehicle().Organization!, "Чужая услуга");
        var stage = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Приемка");
        stage.KindCode = OrganizationScenarioCatalogKindCodes.WorkType;
        var invalidId = Guid.NewGuid();
        var work = new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Date = DateTime.UtcNow, DateTimeCreation = DateTime.UtcNow,
            Details = [
                new() { Name = "  пОЛИРОВКА  ", Price = 1234, Description = "Исторический состав работ" },
                new() { Name = "Архивная мойка", Price = 567 },
                new() { Name = "Комплекс", Price = 789 },
                new() { Name = "Чужая услуга", Price = 999 },
                new() { Name = "Приемка" },
                new() { Name = "Полировка", CatalogItemId = invalidId },
                new() { Name = "Материал", MaterialName = "Полировка", MaterialQuantity = 1 }
            ]
        };
        context.OrganizationScenarioCatalogItems.AddRange(unique, archived, duplicate, duplicateAgain, foreign, stage);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var before = Newtonsoft.Json.Linq.JArray.FromObject(work.Details);
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        var scriptDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "maintenance");
        command.CommandText = await File.ReadAllTextAsync(Path.Combine(scriptDirectory, "audit-service-links.sql"));
        var statuses = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetGuid(1) == work.Id)
                {
                    statuses.Add(reader.GetString(5));
                }
            }
        }

        Assert.Equal(new[] { "can_link", "can_link", "ambiguous", "not_found", "not_found", "invalid_link" }, statuses);
        command.CommandText = await File.ReadAllTextAsync(Path.Combine(scriptDirectory, "link-unambiguous-services.sql"));
        await command.ExecuteNonQueryAsync();
        var saved = await context.Maintenance.AsNoTracking().SingleAsync(item => item.Id == work.Id);
        Assert.Equal(unique.Id, saved.Details[0].CatalogItemId);
        Assert.Equal(archived.Id, saved.Details[1].CatalogItemId);
        Assert.Null(saved.Details[2].CatalogItemId);
        Assert.Null(saved.Details[3].CatalogItemId);
        Assert.Null(saved.Details[4].CatalogItemId);
        Assert.Equal(invalidId, saved.Details[5].CatalogItemId);
        Assert.Null(saved.Details[6].CatalogItemId);
        var after = Newtonsoft.Json.Linq.JArray.FromObject(saved.Details);
        before[0]["CatalogItemId"] = unique.Id;
        before[1]["CatalogItemId"] = archived.Id;
        Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(before, after));
        await command.ExecuteNonQueryAsync();
        var savedAgain = await context.Maintenance.AsNoTracking().SingleAsync(item => item.Id == work.Id);
        Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(after, Newtonsoft.Json.Linq.JArray.FromObject(savedAgain.Details)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CreationAndEditingRejectOnlyActiveOverlapsOnPlannedDay(bool existingCompleted, bool incomingCompleted)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Обслуживание");
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var existing = new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Type = MaintenanceType.Maintenance,
            Status = existingCompleted ? MaintenanceStatus.Completed : MaintenanceStatus.Planned,
            PlannedDate = date, CompletedDate = existingCompleted ? date.AddDays(1) : null,
            Date = existingCompleted ? date.AddDays(1) : date, DateTimeCreation = date,
            Location = "Пост 1", PlannedStartTime = TimeSpan.FromHours(9), PlannedEndTime = TimeSpan.FromHours(11),
            Details = [new() { CatalogItemId = item.Id, Name = item.Name, Price = 1000 }]
        };
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Maintenance.Add(existing);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Status = incomingCompleted ? MaintenanceStatus.Completed : MaintenanceStatus.Planned;
        request.CompletedDate = incomingCompleted ? date.AddDays(2) : null;
        request.Location = "  ПОСТ 1  ";
        request.PlannedStartTime = TimeSpan.FromHours(10);
        request.PlannedEndTime = TimeSpan.FromHours(11);

        if (existingCompleted)
        {
            var overlappingId = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
            var overlappingWork = await context.Maintenance.SingleAsync(work => work.Id == overlappingId);
            overlappingWork.IsDeleted = true;
            await context.SaveChangesAsync();
            Assert.Equal(2, await context.Maintenance.CountAsync(work => work.VehicleId == vehicle.Id));
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request));

            Assert.Contains("уже занят", error.Message);
            Assert.Equal(1, await context.Maintenance.CountAsync(work => work.VehicleId == vehicle.Id));
        }
        request.PlannedStartTime = TimeSpan.FromHours(11);
        request.PlannedEndTime = TimeSpan.FromHours(12);
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var update = new UpdateMaintenanceDto
        {
            Status = request.Status, Type = request.Type, PlannedDate = request.PlannedDate,
            CompletedDate = request.CompletedDate, Location = request.Location,
            PlannedStartTime = TimeSpan.FromHours(10), PlannedEndTime = TimeSpan.FromHours(11),
            Details = request.Details, Performer = null, Mileage = null, Comment = null
        };

        if (existingCompleted)
        {
            await service.Update(vehicle.User.Id, vehicle.Organization.Id, id, update);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.Update(vehicle.User.Id, vehicle.Organization.Id, id, update));

            Assert.Contains("уже занят", error.Message);
        }

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(work => work.Id == id);
        Assert.Equal(TimeSpan.FromHours(existingCompleted ? 10 : 11), saved.PlannedStartTime);
    }

    [Fact]
    public async Task CompleteActionSetsActualDateWithoutMovingPlannedSlot()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Обслуживание");
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.PlannedDate = request.PlannedDate!.Value.AddMonths(-4);
        request.CompletedDate = request.PlannedDate.Value.AddDays(1);
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Null(work.CompletedDate);

        await service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id, new MaintenanceWorkflowActionDto { Action = "complete" });

        Assert.Equal(MaintenanceStatus.Completed, work.Status);
        Assert.Equal(new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc), work.CompletedDate);
        Assert.Equal(request.PlannedDate, work.PlannedDate);
    }

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(1, 4, true)]
    [InlineData(2, 4, true)]
    [InlineData(4, 1, true)]
    [InlineData(1, 3, false)]
    [InlineData(2, 3, false)]
    [InlineData(2, 1, false)]
    [InlineData(3, 1, false)]
    [InlineData(3, 4, false)]
    [InlineData(4, 2, false)]
    [InlineData(4, 3, false)]
    public async Task EditorAllowsOnlySupportedStatusTransitions(int previousStatus, int nextStatus, bool allowed)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Обслуживание");
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var work = new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Type = MaintenanceType.Maintenance,
            Status = (MaintenanceStatus)previousStatus, Date = date, PlannedDate = date,
            CompletedDate = previousStatus == 3 ? date : null, DateTimeCreation = date,
            Details = [new() { Name = item.Name, CatalogItemId = item.Id }]
        };
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var update = new UpdateMaintenanceDto
        {
            Status = (MaintenanceStatus)nextStatus, Type = work.Type, PlannedDate = date, CompletedDate = date,
            Location = null, Performer = null, Mileage = null, Comment = null,
            Details = [new() { Name = item.Name, CatalogItemId = item.Id, Price = null, Description = null }]
        };
        if (!allowed)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(vehicle.User.Id, vehicle.Organization!.Id, work.Id, update));
            Assert.Equal((MaintenanceStatus)previousStatus, work.Status);
            return;
        }

        await service.Update(vehicle.User.Id, vehicle.Organization!.Id, work.Id, update);

        Assert.Equal((MaintenanceStatus)nextStatus, work.Status);
        Assert.Null(work.CompletedDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnCompletedToWorkPreservesDataAndRecalculatesRealCounter(bool hasOtherCounter)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Обслуживание");
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var vehicles = new Mock<IVehicleRepository>();
        vehicles.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<Guid?>(), vehicle.Id)).ReturnsAsync(vehicle);
        vehicles.Setup(value => value.Save()).Returns(async () => { await context.SaveChangesAsync(); });
        var clock = new Mock<ISystemClock>();
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        clock.SetupGet(value => value.UtcNow).Returns(date);
        var subscriptions = Mock.Of<IOrganizationSubscriptionService>();
        var counterService = new VehicleCounterCommandService(vehicles.Object, clock.Object, context, subscriptions,
            new VehicleHistoryWriter(context, clock.Object, vehicles.Object, subscriptions));
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle, counterService);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.PlannedDate = date.AddDays(-1);
        request.Status = MaintenanceStatus.Completed;
        request.CompletedDate = date;
        request.Mileage = 200;
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        await new ApplicationUnitOfWork(context).ExecuteInTransactionAsync(() => service.AddPayment(
            vehicle.User.Id, vehicle.Organization.Id, id, new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 500 }));
        if (hasOtherCounter)
        {
            context.VehicleCounterEntries.Add(new VehicleCounterEntry
            {
                Vehicle = vehicle, User = vehicle.User, Value = 100, RecordedAt = date.AddDays(-5)
            });
            await context.SaveChangesAsync();
        }
        var unitOfWork = new ApplicationUnitOfWork(context);

        await unitOfWork.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
            new MaintenanceWorkflowActionDto { Action = "return_to_work", Reason = "Нажали по ошибке" }));

        context.ChangeTracker.Clear();
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        var car = await context.Vehicles.SingleAsync(value => value.Id == vehicle.Id);
        Assert.Equal(MaintenanceStatus.InProgress, work.Status);
        Assert.Null(work.CompletedDate);
        Assert.Equal(request.PlannedDate, work.PlannedDate);
        Assert.Equal(500, work.PaidAmount);
        Assert.Equal(item.Id, Assert.Single(work.Details).CatalogItemId);
        Assert.Equal(hasOtherCounter ? 100 : (int?)null, car.CurrentMileage);
        Assert.True((await context.VehicleCounterEntries.SingleAsync(value => value.MaintenanceId == id)).IsDeleted);
        var history = await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.Contains(history, value => value.Description != null && value.Description.Contains("Нажали по ошибке"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(() =>
            service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id, new MaintenanceWorkflowActionDto { Action = "return_to_work" })));
    }

    [Fact]
    public async Task ReturnToPlannedAndBackToWorkPreservesSlotAndHistory()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Обслуживание");
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Location = "Пост 1";
        request.PlannedStartTime = TimeSpan.FromHours(9);
        request.PlannedEndTime = TimeSpan.FromHours(11);
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var transaction = new ApplicationUnitOfWork(context);
        foreach (var action in new[] { "start", "return_to_planned", "start" })
        {
            await transaction.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
                new MaintenanceWorkflowActionDto { Action = action }));
        }
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Equal(MaintenanceStatus.InProgress, work.Status);
        Assert.Equal(request.PlannedDate, work.PlannedDate);
        Assert.Equal(request.PlannedStartTime, work.PlannedStartTime);
        Assert.Null(work.CompletedDate);
    }

    [Fact]
    public async Task FailedSynchronizationRollsBackReturnAndItsHistory()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Обслуживание");
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Status = MaintenanceStatus.Completed;
        request.CompletedDate = request.PlannedDate;
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var previousHistory = (await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id)).Count;
        var transaction = new ApplicationUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(async () =>
        {
            await service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id, new MaintenanceWorkflowActionDto { Action = "return_to_work" });
            throw new InvalidOperationException("Ошибка синхронизации планировщика");
        }));

        var saved = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Equal(MaintenanceStatus.Completed, saved.Status);
        Assert.Equal(request.CompletedDate, saved.CompletedDate);
        Assert.Equal(previousHistory, (await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id)).Count);
    }

    [Theory]
    [InlineData("return_to_work")]
    [InlineData("handover")]
    public async Task ConcurrentWorkflowCommandsDoNotDuplicateHistory(string action)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Обслуживание");
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Status = MaintenanceStatus.Completed;
        request.CompletedDate = request.PlannedDate;
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        if (action == "handover")
        {
            await service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
                new MaintenanceWorkflowActionDto { Action = "mark_ready" });
        }

        var previousHistory = (await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id)).Count;

        async Task<bool> ChangeAsync()
        {
            await using var separateContext = fixture.CreateContext();
            var separateService = MaintenanceServiceCatalogTests.CreateService(separateContext, vehicle);
            try
            {
                await new ApplicationUnitOfWork(separateContext).ExecuteInTransactionAsync(() =>
                    separateService.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
                        new MaintenanceWorkflowActionDto { Action = action }));

                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(ChangeAsync(), ChangeAsync());

        Assert.Single(results, value => value);
        Assert.Equal(previousHistory + 1, (await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id)).Count);
    }

    [Theory]
    [InlineData("return_to_planned", MaintenanceStatus.InProgress)]
    [InlineData("return_to_work", MaintenanceStatus.Completed)]
    public async Task ReverseActionRejectsOccupiedSlotWithoutSaving(string action, MaintenanceStatus status)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Обслуживание");
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var work = new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Type = MaintenanceType.Maintenance, Status = status,
            Date = date, PlannedDate = date, CompletedDate = status == MaintenanceStatus.Completed ? date : null,
            Location = "Пост 1", PlannedStartTime = TimeSpan.FromHours(9), PlannedEndTime = TimeSpan.FromHours(11),
            DateTimeCreation = date, Details = [new() { CatalogItemId = item.Id, Name = item.Name }]
        };
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Maintenance.AddRange(work, new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Type = work.Type, Status = MaintenanceStatus.Planned,
            Date = date, PlannedDate = date, DateTimeCreation = date, Location = work.Location,
            PlannedStartTime = TimeSpan.FromHours(10), PlannedEndTime = TimeSpan.FromHours(11), Details = []
        });
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var transaction = new ApplicationUnitOfWork(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() =>
            service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization!.Id, work.Id, new MaintenanceWorkflowActionDto { Action = action })));

        Assert.Contains("уже занят", error.Message);
        Assert.Equal(status, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).Status);
        Assert.Empty(await service.GetHistory(vehicle.User.Id, vehicle.Organization!.Id, work.Id));
    }

    [Fact]
    public async Task MultiDayBookingBlocksIntermediateDaysAndAllowsAdjacentBooking()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Location = "Пост 1";
        request.PlannedDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        request.PlannedEndDate = request.PlannedDate.Value.AddDays(2);
        request.PlannedStartTime = TimeSpan.FromHours(10);
        request.PlannedEndTime = TimeSpan.FromHours(9);
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var saved = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.Equal(request.PlannedEndDate, saved.PlannedEndDate);
        var calendar = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));
        var middle = request.PlannedDate.Value.AddDays(1);
        Assert.Equal(id, Assert.Single(await calendar.GetMaintenanceCalendarAsync(vehicle.Organization.Id, middle, middle, includeAllStatuses: true)).Id);
        var occupied = await service.GetOccupiedSlots(vehicle.User.Id, vehicle.Organization.Id, DateOnly.FromDateTime(middle), "Пост 1");
        Assert.Equal(0, Assert.Single(occupied).StartMinute);
        Assert.Equal(1440, occupied[0].EndMinute);

        var conflict = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        conflict.Location = request.Location;
        conflict.PlannedDate = middle;
        conflict.PlannedStartTime = TimeSpan.FromHours(12);
        conflict.PlannedEndTime = TimeSpan.FromHours(13);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, conflict));
        conflict.PlannedDate = request.PlannedEndDate;
        conflict.PlannedStartTime = request.PlannedEndTime;
        conflict.PlannedEndTime = TimeSpan.FromHours(10);
        await service.Create(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, conflict);
        Assert.Equal(2, await context.Maintenance.CountAsync(x => x.VehicleId == vehicle.Id));

        var unitOfWork = new ApplicationUnitOfWork(context);
        var moved = request.PlannedDate.Value.AddDays(10);
        await unitOfWork.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
            new MaintenanceWorkflowActionDto { Action = "reschedule", PlannedDate = moved, PlannedEndDate = moved.AddDays(2), PlannedStartTime = request.PlannedStartTime, PlannedEndTime = request.PlannedEndTime }));
        saved = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.Equal(moved.AddDays(2), saved.PlannedEndDate);
        Assert.Empty(await service.GetOccupiedSlots(vehicle.User.Id, vehicle.Organization.Id, DateOnly.FromDateTime(middle), "Пост 1"));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(-1, 10, 11)]
    [InlineData(0, 10, 10)]
    [InlineData(0, 11, 10)]
    [InlineData(2, 10, -1)]
    [InlineData(2, -1, 10)]
    public async Task RejectsInvalidPlannedPeriodWithoutSaving(int endDay, int startHour, int endHour)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.PlannedEndDate = request.PlannedDate!.Value.AddDays(endDay);
        request.PlannedStartTime = startHour < 0 ? null : TimeSpan.FromHours(startHour);
        request.PlannedEndTime = endHour < 0 ? null : TimeSpan.FromHours(endHour);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request));
        Assert.False(await context.Maintenance.AnyAsync(x => x.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task ConcurrentMultiDayBookingsCannotReserveTheSamePost()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();

        async Task<bool> BookAsync(int offset)
        {
            await using var separateContext = fixture.CreateContext();
            var service = MaintenanceServiceCatalogTests.CreateService(separateContext, vehicle);
            var transaction = new ApplicationUnitOfWork(separateContext);
            var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
            request.Location = "Пост 1";
            request.PlannedDate = request.PlannedDate!.Value.AddDays(offset);
            request.PlannedEndDate = request.PlannedDate.Value.AddDays(2);
            request.PlannedStartTime = TimeSpan.FromHours(10);
            request.PlannedEndTime = TimeSpan.FromHours(18);
            try
            {
                await transaction.ExecuteInTransactionAsync(() => service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request));

                return true;
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("уже занят"))
            {
                return false;
            }
        }

        var results = await Task.WhenAll(BookAsync(0), BookAsync(1));
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
        Assert.Equal(1, await context.Maintenance.CountAsync(value => value.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task MultiDayEditKeepsPeriodAndRejectsConflictingChanges()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Location = "Пост 1";
        request.PlannedEndDate = request.PlannedDate!.Value.AddDays(2);
        request.PlannedStartTime = TimeSpan.FromHours(10);
        request.PlannedEndTime = TimeSpan.FromHours(18);
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var other = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        other.Location = "Пост 2";
        other.PlannedDate = request.PlannedDate.Value.AddDays(1);
        other.PlannedStartTime = TimeSpan.FromHours(12);
        other.PlannedEndTime = TimeSpan.FromHours(13);
        var otherId = await service.Create(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, other);
        var update = new UpdateMaintenanceDto
        {
            Status = request.Status, Type = request.Type, PlannedDate = request.PlannedDate,
            PlannedStartTime = request.PlannedStartTime, PlannedEndTime = request.PlannedEndTime,
            CompletedDate = null, Location = request.Location, Performer = null, Mileage = null,
            Comment = "Обновили комментарий", Details = request.Details
        };
        var transaction = new ApplicationUnitOfWork(context);
        await transaction.ExecuteInTransactionAsync(() => service.Update(vehicle.User.Id, vehicle.Organization.Id, id, update));
        var saved = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.Equal(request.PlannedEndDate, saved.PlannedEndDate);
        Assert.Equal(update.Comment, saved.Comment);

        update.Location = "Пост 2";
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(
            () => service.Update(vehicle.User.Id, vehicle.Organization.Id, id, update)));
        saved = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.Equal("Пост 1", saved.Location);
        Assert.Equal(request.PlannedEndDate, saved.PlannedEndDate);
        Assert.Empty(await service.GetOccupiedSlots(vehicle.User.Id, vehicle.Organization.Id,
            DateOnly.FromDateTime(other.PlannedDate.Value), "Пост 1", id));
        Assert.NotEqual(id, otherId);
    }

    [Fact]
    public async Task HandoverAndUndoPreserveCompletionPaymentAndCounterUntilReturnToWork()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();
        var counters = new Mock<IVehicleCounterCommandService>();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle, counters.Object);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.Mileage = 15000;
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        await new ApplicationUnitOfWork(context).ExecuteInTransactionAsync(() => service.AddPayment(
            vehicle.User.Id, vehicle.Organization.Id, id, new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 500 }));
        var transaction = new ApplicationUnitOfWork(context);
        await transaction.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
            new MaintenanceWorkflowActionDto { Action = "complete" }));
        var ready = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.NotNull(ready.ReadyForHandoverAt);
        Assert.Null(ready.HandedOverAt);
        var counterCalls = counters.Invocations.Count;

        await transaction.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
            new MaintenanceWorkflowActionDto { Action = "handover" }));
        var issued = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.NotNull(issued.HandedOverAt);
        Assert.Equal(MaintenanceStatus.Completed, issued.Status);
        Assert.Equal(ready.CompletedDate, issued.CompletedDate);
        Assert.Equal(ready.PlannedDate, issued.PlannedDate);
        Assert.Equal(500, issued.PaidAmount);
        Assert.Equal(counterCalls, counters.Invocations.Count);

        foreach (var action in new[] { "handover", "return_to_work", "undo_handover" })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(
                () => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id, new MaintenanceWorkflowActionDto { Action = action })));
        }

        await transaction.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
            new MaintenanceWorkflowActionDto { Action = "undo_handover", Reason = "Нажали по ошибке" }));
        var returned = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.Null(returned.HandedOverAt);
        Assert.Equal(ready.ReadyForHandoverAt, returned.ReadyForHandoverAt);
        Assert.Equal(ready.CompletedDate, returned.CompletedDate);
        Assert.Equal(counterCalls, counters.Invocations.Count);
        Assert.Contains(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == id).ToListAsync(),
            value => value.Description != null && value.Description.Contains("Нажали по ошибке"));

        await transaction.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
            new MaintenanceWorkflowActionDto { Action = "return_to_work" }));
        returned = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.Equal(MaintenanceStatus.InProgress, returned.Status);
        Assert.Null(returned.ReadyForHandoverAt);
        Assert.Null(returned.HandedOverAt);
        Assert.Null(returned.CompletedDate);
        Assert.Equal(500, returned.PaidAmount);
        Assert.True(counters.Invocations.Count > counterCalls);
    }

    [Theory]
    [InlineData("mark_ready", MaintenanceStatus.Planned)]
    [InlineData("handover", MaintenanceStatus.Planned)]
    [InlineData("handover", MaintenanceStatus.InProgress)]
    [InlineData("handover", MaintenanceStatus.Cancelled)]
    [InlineData("handover", MaintenanceStatus.Completed)]
    [InlineData("undo_handover", MaintenanceStatus.Completed)]
    public async Task HandoverRejectsInvalidState(string action, MaintenanceStatus status)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var work = new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Status = status, Details = [],
            Date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc)
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var transaction = new ApplicationUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() =>
            service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization!.Id, work.Id,
                new MaintenanceWorkflowActionDto { Action = action, Reason = "Проверка" })));

        var saved = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(status, saved.Status);
        Assert.Null(saved.ReadyForHandoverAt);
        Assert.Null(saved.HandedOverAt);
    }

    [Fact]
    public async Task LegacyCompletedWorkRequiresExplicitReadinessAndPersonalWorkCannotBeHandedOver()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var completedDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var work = new MaintenanceEntity
        {
            Vehicle = vehicle, User = vehicle.User, Status = MaintenanceStatus.Completed,
            Date = completedDate, CompletedDate = completedDate, Details = []
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var transaction = new ApplicationUnitOfWork(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() =>
            service.ChangeWorkflow(vehicle.User.Id, null, work.Id, new MaintenanceWorkflowActionDto { Action = "mark_ready" })));

        await transaction.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization!.Id, work.Id,
            new MaintenanceWorkflowActionDto { Action = "mark_ready" }));

        var saved = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.NotNull(saved.ReadyForHandoverAt);
        Assert.Null(saved.HandedOverAt);
        Assert.Equal(completedDate, saved.CompletedDate);
    }

    [Fact]
    public async Task CalendarMoveChangesPostAndPeriodWithHistoryAndPreservesServices()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var serviceItem = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        var post = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Пост 2");
        post.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(serviceItem, post);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(serviceItem.Id);
        request.Location = "Пост 1";
        request.PlannedStartTime = TimeSpan.FromHours(22);
        request.PlannedEndTime = TimeSpan.FromHours(2);
        request.PlannedEndDate = request.PlannedDate!.Value.AddDays(1);
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var original = await context.Maintenance.SingleAsync(value => value.Id == id);
        var version = original.Version;
        var detail = Assert.Single(original.Details);
        var originalDetail = (detail.CatalogItemId, detail.Name, detail.Price, detail.Quantity);
        var transaction = new ApplicationUnitOfWork(context);
        var movedDate = request.PlannedDate.Value.AddDays(3);

        await transaction.ExecuteInTransactionAsync(() => service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
            new MaintenanceWorkflowActionDto
            {
                Action = "reschedule", Version = version, Location = post.Name,
                PlannedDate = movedDate, PlannedEndDate = movedDate.AddDays(1),
                PlannedStartTime = TimeSpan.FromHours(23), PlannedEndTime = TimeSpan.FromHours(3)
            }));

        var saved = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Equal(post.Name, saved.Location);
        Assert.Equal(movedDate, saved.PlannedDate);
        Assert.Equal(movedDate.AddDays(1), saved.PlannedEndDate);
        Assert.Equal(TimeSpan.FromHours(23), saved.PlannedStartTime);
        Assert.Equal(MaintenanceStatus.Planned, saved.Status);
        Assert.Null(saved.CompletedDate);
        var savedDetail = Assert.Single(saved.Details);
        Assert.Equal(originalDetail, (savedDetail.CatalogItemId, savedDetail.Name, savedDetail.Price, savedDetail.Quantity));
        Assert.NotEqual(version, saved.Version);
        var history = await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id);
        Assert.Contains(history, value => value.Description != null && value.Description.Contains("Пост 1") && value.Description.Contains("Пост 2"));
        var calendar = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));
        var calendarItem = Assert.Single(await calendar.GetMaintenanceCalendarAsync(vehicle.Organization.Id, movedDate, movedDate, includeAllStatuses: true));
        Assert.Equal(saved.Version, calendarItem.Version);

        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() =>
            service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id,
                new MaintenanceWorkflowActionDto { Action = "reschedule", Version = version, PlannedDate = movedDate.AddDays(5), Location = "" })));
        Assert.Contains("уже изменена", stale.Message);
        Assert.Equal(post.Name, (await context.Maintenance.SingleAsync(value => value.Id == id)).Location);
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("foreign")]
    [InlineData("inactive")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task CalendarMoveRejectsUnavailablePostOrClosedRecordWithoutPartialSave(string scenario)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var serviceItem = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        var post = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Пост 2");
        post.KindCode = OrganizationScenarioCatalogKindCodes.WorkPost;
        post.IsActive = scenario != "inactive";
        if (scenario == "foreign") post.Organization = MaintenanceServiceCatalogTests.CreateVehicle().Organization!;
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(serviceItem, post);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(serviceItem.Id);
        request.Location = "Пост 1";
        request.PlannedStartTime = TimeSpan.FromHours(10);
        request.PlannedEndTime = TimeSpan.FromHours(11);
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        if (scenario is "completed" or "cancelled")
        {
            work.Status = scenario == "completed" ? MaintenanceStatus.Completed : MaintenanceStatus.Cancelled;
            await context.SaveChangesAsync();
        }
        if (scenario == "occupied")
        {
            var other = MaintenanceServiceCatalogTests.CreateRequest(serviceItem.Id);
            other.Location = post.Name;
            other.PlannedStartTime = TimeSpan.FromHours(10);
            other.PlannedEndTime = TimeSpan.FromHours(11);
            await service.Create(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, other);
        }
        var version = work.Version;
        var status = work.Status;
        var historyCount = (await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id)).Count;
        var transaction = new ApplicationUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteInTransactionAsync(() =>
            service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id, new MaintenanceWorkflowActionDto
            {
                Action = "reschedule", Version = version, Location = post.Name, PlannedDate = request.PlannedDate,
                PlannedEndDate = request.PlannedDate, PlannedStartTime = TimeSpan.FromHours(10.5), PlannedEndTime = TimeSpan.FromHours(11.5)
            })));

        var saved = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Equal("Пост 1", saved.Location);
        Assert.Equal(TimeSpan.FromHours(10), saved.PlannedStartTime);
        Assert.Equal(status, saved.Status);
        Assert.Equal(historyCount, (await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id)).Count);
    }

    [Fact]
    public async Task EditingMaintenancePreservesFullAuditAndRecordsPerformerRemoval()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        vehicle.User.Email = "audit@example.test";
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.Add(item);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        var firstEmployee = new OrganizationEmployee { OrganizationId = vehicle.Organization!.Id, Name = "Андрей" };
        var secondEmployee = new OrganizationEmployee { OrganizationId = vehicle.Organization.Id, Name = "Сергей" };
        context.OrganizationEmployees.AddRange(firstEmployee, secondEmployee);
        await context.SaveChangesAsync();
        request.EmployeeId = firstEmployee.Id;
        request.Details.Single().Description = new string('Г', 900);
        request.Comment = new string('А', 900) + "Первоначальное примечание";
        var id = await service.Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        var update = new UpdateMaintenanceDto
        {
            Status = work.Status, Type = work.Type, PlannedDate = work.PlannedDate, CompletedDate = null,
            Location = "Пост 2", Performer = null, EmployeeId = secondEmployee.Id, Mileage = 15000,
            Comment = new string('Б', 900) + "Конец нового примечания", LaborCostAmount = 550,
            Details = [new CreateMaintenanceDetailDto
            {
                CatalogItemId = item.Id, Name = "Полировка", Quantity = 2, Price = 2000,
                Description = new string('В', 900) + "Конец описания услуги"
            }]
        };
        var transaction = new ApplicationUnitOfWork(context);

        await transaction.ExecuteInTransactionAsync(() => service.Update(vehicle.User.Id, vehicle.Organization.Id, id, update));

        var history = await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == id).ToListAsync();
        var fields = Assert.Single(history, value => value.EventType == "fields_changed");
        Assert.Equal(vehicle.User.Id, fields.UserId);
        Assert.Equal("audit@example.test", fields.ActorDisplayName);
        Assert.Equal(new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc), fields.CreatedAt);
        Assert.Contains($"Исполнитель: Андрей [{firstEmployee.Id}] -> Сергей [{secondEmployee.Id}]", fields.Description);
        Assert.Contains("Первоначальное примечание", fields.Description);
        Assert.Contains("Конец нового примечания", fields.Description);
        Assert.Contains("Себестоимость труда", fields.Description);
        Assert.True(fields.Description!.Length > 1800);
        var details = Assert.Single(history, value => value.EventType == "details_changed");
        Assert.Contains("Конец описания услуги", details.Description);
        Assert.Contains("количество: 1 -> 2", details.Description);
        Assert.Contains("стоимость:", details.Description);
        Assert.False(context.Database.HasPendingModelChanges());

        await transaction.ExecuteInTransactionAsync(() => service.Update(vehicle.User.Id, vehicle.Organization.Id, id, update));
        Assert.Equal(history.Count, await context.MaintenanceHistoryEvents.CountAsync(value => value.MaintenanceId == id));
        update.EmployeeId = null;
        await transaction.ExecuteInTransactionAsync(() => service.Update(vehicle.User.Id, vehicle.Organization.Id, id, update));
        Assert.Contains(await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id),
            value => value.Description != null && value.Description.Contains($"Исполнитель: Сергей [{secondEmployee.Id}] ->"));
        Assert.Contains(await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, id), value => value.Id == fields.Id);
    }

    [Theory]
    [InlineData("catalog", "материал из справочника")]
    [InlineData("unit", "единица расхода")]
    [InlineData("cost", "себестоимость расхода")]
    public async Task EditingOnlyMaterialIdentityOrUnitOrTotalCostIsAudited(string change, string expected)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        vehicle.Organization = null;
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var materialId = Guid.NewGuid();
        var material = new CreateMaintenanceDetailDto
        {
            Name = "Материал", Description = null, Price = 0, MaterialCatalogItemId = materialId,
            MaterialName = "Шампунь", MaterialQuantity = 5, MaterialUnit = "мл", MaterialUnitCost = 10, MaterialTotalCost = 50
        };
        var request = MaintenanceServiceCatalogTests.CreateRequest(null);
        request.Details = [material];
        var id = await service.Create(vehicle.User.Id, null, vehicle.Id, request);
        var work = await context.Maintenance.SingleAsync(value => value.Id == id);
        if (change == "catalog")
        {
            material.MaterialCatalogItemId = Guid.NewGuid();
        }
        if (change == "unit")
        {
            material.MaterialUnit = "л";
        }
        if (change == "cost")
        {
            material.MaterialTotalCost = 75;
        }
        var transaction = new ApplicationUnitOfWork(context);

        await transaction.ExecuteInTransactionAsync(() => service.Update(vehicle.User.Id, null, id, new UpdateMaintenanceDto
        {
            Status = work.Status, Type = work.Type, PlannedDate = work.PlannedDate, CompletedDate = null,
            Location = null, Performer = null, Mileage = null, Comment = null, Details = [material]
        }));

        var history = Assert.Single(await context.MaintenanceHistoryEvents
            .Where(value => value.MaintenanceId == id && value.EventType == "details_changed").ToListAsync());
        Assert.Contains(expected, history.Description);
        Assert.Contains(" -> ", history.Description);
        Assert.Equal(vehicle.User.Id, history.UserId);
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(value => value.Id == id);
        Assert.Equal(material.MaterialCatalogItemId, saved.Details[0].MaterialCatalogItemId);
        Assert.Equal(material.MaterialUnit, saved.Details[0].MaterialUnit);
        Assert.Equal(material.MaterialTotalCost, saved.Details[0].MaterialTotalCost);
    }

    [Fact]
    public async Task EmployeesAreIndependentFromUsersAndScopedToOrganization()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var other = MaintenanceServiceCatalogTests.CreateVehicle();
        context.Vehicles.AddRange(vehicle, other);
        await context.SaveChangesAsync();
        var userCount = await context.Users.CountAsync();
        var memberCount = await context.OrganizationMembers.CountAsync();
        var access = new Mock<IOrganizationSubscriptionService>();
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        var directory = new OrganizationEmployeeService(context, access.Object, permissions.Object);
        var organizationId = vehicle.Organization!.Id;
        var id = await directory.SaveAsync(vehicle.User.Id, organizationId, null,
            new OrganizationEmployeeDto { Name = "  Мастер без аккаунта  ", Phone = "+7 900 000 00 00" });
        var employee = Assert.Single(await directory.GetAsync(vehicle.User.Id, organizationId));
        Assert.Equal(id, employee.Id);
        Assert.Equal("Мастер без аккаунта", employee.Name);
        Assert.Equal(userCount, await context.Users.CountAsync());
        Assert.Equal(memberCount, await context.OrganizationMembers.CountAsync());
        Assert.Empty(await directory.GetAsync(other.User.Id, other.Organization!.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => directory.SaveAsync(other.User.Id, other.Organization.Id, id, employee));
        employee.IsActive = false;
        await directory.SaveAsync(vehicle.User.Id, organizationId, id, employee);
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.SaveAsync(vehicle.User.Id, organizationId, id, employee));
        Assert.False((await directory.GetAsync(vehicle.User.Id, organizationId)).Single().IsActive);
        access.Verify(value => value.EnsureWorkspaceCanWriteAsync(vehicle.User.Id, organizationId, OrganizationPermissionCodes.EmployeesManage), Times.AtLeastOnce);
        access.Setup(value => value.EnsureWorkspaceCanWriteAsync(vehicle.User.Id, organizationId, OrganizationPermissionCodes.EmployeesManage))
            .ThrowsAsync(new InvalidOperationException("Нет права"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.SaveAsync(vehicle.User.Id, organizationId, null, new OrganizationEmployeeDto { Name = "Запрещён" }));
        Assert.Equal(2, await context.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organizationId && item.TargetType == "employee"));
    }

    [Fact]
    public async Task EmployeeAssignmentsRejectOtherOrganizationsAndPreserveAuditForIdenticalNames()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        vehicle.User.Email = "administrator@example.test";
        var other = MaintenanceServiceCatalogTests.CreateVehicle();
        var serviceItem = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        var first = new OrganizationEmployee { Organization = vehicle.Organization!, Name = "Иван" };
        var second = new OrganizationEmployee { Organization = vehicle.Organization!, Name = "Иван" };
        var outsider = new OrganizationEmployee { Organization = other.Organization!, Name = "Чужой" };
        var archived = new OrganizationEmployee { Organization = vehicle.Organization!, Name = "Бывший", IsActive = false };
        context.Vehicles.AddRange(vehicle, other);
        context.OrganizationEmployees.AddRange(first, second, outsider, archived);
        context.OrganizationScenarioCatalogItems.Add(serviceItem);
        await context.SaveChangesAsync();
        var organizationId = vehicle.Organization!.Id;
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(serviceItem.Id);
        request.EmployeeId = first.Id;
        request.Performer = "Подмена имени";
        request.Details.Single().EmployeeId = first.Id;
        request.Details.Single().EmployeeName = "Подмена имени";
        var id = await service.Create(vehicle.User.Id, organizationId, vehicle.Id, request);
        var work = await context.Maintenance.SingleAsync(item => item.Id == id);
        Assert.Equal("Иван", work.Performer);
        Assert.Equal("Иван", work.Details[0].EmployeeName);
        var unitOfWork = new ApplicationUnitOfWork(context);
        var version = work.Version;
        await unitOfWork.ExecuteInTransactionAsync(() => service.AssignEmployee(vehicle.User.Id, organizationId, id,
            new AssignMaintenanceEmployeeDto { EmployeeId = second.Id, Version = version }));
        var history = await service.GetHistory(vehicle.User.Id, organizationId, id);
        var reassigned = Assert.Single(history, item => item.EventType == "fields_changed");
        Assert.Contains($"Иван [{first.Id}] -> Иван [{second.Id}]", reassigned.Description);
        Assert.Equal("administrator@example.test", reassigned.ActorDisplayName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(() => service.AssignEmployee(vehicle.User.Id, organizationId, id,
            new AssignMaintenanceEmployeeDto { EmployeeId = null, Version = version })));

        foreach (var invalidId in new[] { outsider.Id, archived.Id, Guid.NewGuid() })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(() => service.AssignEmployee(vehicle.User.Id, organizationId, id,
                new AssignMaintenanceEmployeeDto { EmployeeId = invalidId, Version = work.Version })));
            var invalidRequest = MaintenanceServiceCatalogTests.CreateRequest(serviceItem.Id);
            invalidRequest.Details.Single().EmployeeId = invalidId;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(vehicle.User.Id, organizationId, vehicle.Id, invalidRequest));
        }

        work = await context.Maintenance.SingleAsync(item => item.Id == id);
        second = await context.OrganizationEmployees.SingleAsync(item => item.Id == second.Id);
        var services = new UpdateMaintenanceServicesDto
        {
            Version = work.Version,
            Details = [new CreateMaintenanceDetailDto { CatalogItemId = serviceItem.Id, Name = serviceItem.Name, Price = 1000, Description = null, EmployeeId = second.Id }]
        };
        await unitOfWork.ExecuteInTransactionAsync(() => service.UpdateServices(vehicle.User.Id, organizationId, id, services));
        var rowEvent = Assert.Single(await service.GetHistory(vehicle.User.Id, organizationId, id), item => item.EventType == "details_changed");
        Assert.Contains($"Иван [{first.Id}] -> Иван [{second.Id}]", rowEvent.Description);
        second.IsActive = false;
        second.Name = "Новое имя в справочнике";
        await context.SaveChangesAsync();
        services.Version = work.Version;
        await unitOfWork.ExecuteInTransactionAsync(() => service.UpdateServices(vehicle.User.Id, organizationId, id, services));
        Assert.Equal("Иван", work.Details[0].EmployeeName);
        await unitOfWork.ExecuteInTransactionAsync(() => service.AssignEmployee(vehicle.User.Id, organizationId, id,
            new AssignMaintenanceEmployeeDto { EmployeeId = null, Version = work.Version }));
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(item => item.Id == id);
        Assert.Null(saved.EmployeeId);
        Assert.Null(saved.Performer);
        Assert.Equal(second.Id, saved.Details[0].EmployeeId);
        Assert.Contains(await service.GetHistory(vehicle.User.Id, organizationId, id), item => item.Id == reassigned.Id);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("mark_ready")]
    [InlineData("handover")]
    public async Task ServiceStationRequiresCompletedOperationsBeforeCompletionAndHandover(string action)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        vehicle.Organization!.PrimaryScenarioCode = "service-station";
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization, "Ремонт");
        item.ScenarioCode = "service-station";
        var employee = new OrganizationEmployee { Organization = vehicle.Organization, Name = "Механик" };
        context.OrganizationScenarioCatalogItems.Add(item);
        context.OrganizationEmployees.Add(employee);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var service = MaintenanceServiceCatalogTests.CreateService(context, vehicle);
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.EmployeeId = employee.Id;
        if (action != "complete")
        {
            request.Status = MaintenanceStatus.Completed;
            request.CompletedDate = request.PlannedDate;
        }

        var id = await service.Create(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, request);
        var work = await context.Maintenance.SingleAsync(maintenance => maintenance.Id == id);
        if (action == "handover")
        {
            work.ReadyForHandoverAt = request.PlannedDate;
            await context.SaveChangesAsync();
        }

        var command = new MaintenanceWorkflowActionDto { Action = action };
        var historyCount = await context.MaintenanceHistoryEvents.CountAsync(entry => entry.MaintenanceId == id);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id, command));

        Assert.Contains("Все работы должны быть завершены", error.Message);
        Assert.Equal(request.Status, work.Status);
        Assert.Null(work.HandedOverAt);
        Assert.Equal(historyCount, await context.MaintenanceHistoryEvents.CountAsync(entry => entry.MaintenanceId == id));
        var operation = new MaintenanceWorkOperation
        {
            Id = Assert.Single(work.Details).DetailId,
            MaintenanceId = id,
            OrganizationId = vehicle.Organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        };
        context.MaintenanceWorkOperations.Add(operation);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id, command));
        operation.Status = MaintenanceWorkStatus.Completed;
        await context.SaveChangesAsync();
        await service.ChangeWorkflow(vehicle.User.Id, vehicle.Organization.Id, id, command);

        Assert.Equal(MaintenanceStatus.Completed, work.Status);
        Assert.NotNull(work.ReadyForHandoverAt);
        Assert.Equal(action == "handover", work.HandedOverAt.HasValue);
        Assert.Equal(historyCount + 1, await context.MaintenanceHistoryEvents.CountAsync(entry => entry.MaintenanceId == id));
    }
    [Theory]
    [InlineData(MaintenanceStatus.Planned)]
    [InlineData(MaintenanceStatus.Completed)]
    [InlineData(MaintenanceStatus.Cancelled)]
    public async Task PlannedDateMoveReloadsConcurrentChangesBeforeSaving(MaintenanceStatus concurrentStatus)
    {
        await using var seedContext = fixture.CreateContext();
        var vehicle = MaintenanceServiceCatalogTests.CreateVehicle();
        var item = MaintenanceServiceCatalogTests.CreateItem(vehicle.Organization!, "Полировка");
        seedContext.Vehicles.Add(vehicle);
        seedContext.OrganizationScenarioCatalogItems.Add(item);
        await seedContext.SaveChangesAsync();
        var request = MaintenanceServiceCatalogTests.CreateRequest(item.Id);
        request.PlannedEndDate = request.PlannedDate!.Value.AddDays(2);
        request.PlannedStartTime = TimeSpan.FromHours(10);
        request.PlannedEndTime = TimeSpan.FromHours(11);
        var id = await MaintenanceServiceCatalogTests.CreateService(seedContext, vehicle)
            .Create(vehicle.User.Id, vehicle.Organization!.Id, vehicle.Id, request);
        var detailId = Assert.Single((await seedContext.Maintenance.SingleAsync(work => work.Id == id)).Details).DetailId;
        var rowLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var editContext = fixture.CreateContext();
        await using var moveContext = fixture.CreateContext();
        var editTask = new ApplicationUnitOfWork(editContext).ExecuteInTransactionAsync(async () =>
        {
            await editContext.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Maintenance" WHERE "Id" = {id} FOR UPDATE""");
            rowLocked.SetResult();
            await staleRead.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var work = await editContext.Maintenance.SingleAsync(maintenance => maintenance.Id == id);
            work.Comment = "Комментарий из параллельного изменения";
            work.Details.Single().Price = 4321;
            work.Status = concurrentStatus;
            work.CompletedDate = concurrentStatus == MaintenanceStatus.Completed ? request.PlannedDate : null;
            work.PlannedEndDate = request.PlannedDate.Value.AddDays(3);
            work.PlannedStartTime = TimeSpan.FromHours(12);
            work.PlannedEndTime = TimeSpan.FromHours(14);
            await editContext.SaveChangesAsync();
        });
        await rowLocked.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var moveService = MaintenanceServiceCatalogTests.CreateService(moveContext, vehicle, onMaintenanceLoaded: () =>
        {
            staleRead.TrySetResult();
        });
        var moveRequest = new UpdateMaintenancePlannedDateDto { PlannedDate = request.PlannedDate.Value.AddDays(5) };
        var moveTask = new ApplicationUnitOfWork(moveContext).ExecuteInTransactionAsync(async () =>
        {
            if (concurrentStatus == MaintenanceStatus.Planned)
            {
                Assert.Equal(MaintenanceStatus.Planned, await moveService.UpdatePlannedDate(
                    vehicle.User.Id, vehicle.Organization.Id, id, moveRequest));

                return;
            }

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => moveService.UpdatePlannedDate(
                vehicle.User.Id, vehicle.Organization.Id, id, moveRequest));

            Assert.Contains("Нельзя переносить выполненное или отмененное", error.Message);
        });
        await Task.WhenAll(editTask, moveTask).WaitAsync(TimeSpan.FromSeconds(60));
        seedContext.ChangeTracker.Clear();
        var saved = await seedContext.Maintenance.SingleAsync(work => work.Id == id);
        var expectedDate = concurrentStatus == MaintenanceStatus.Planned ? moveRequest.PlannedDate : request.PlannedDate.Value;

        Assert.Equal(concurrentStatus, saved.Status);
        Assert.Equal(expectedDate, saved.PlannedDate);
        Assert.Equal(expectedDate.AddDays(3), saved.PlannedEndDate);
        Assert.Equal(TimeSpan.FromHours(12), saved.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(14), saved.PlannedEndTime);
        Assert.Equal("Комментарий из параллельного изменения", saved.Comment);
        Assert.Equal(detailId, Assert.Single(saved.Details).DetailId);
        Assert.Equal(4321, saved.Details.Single().Price);
    }

    private static async Task WaitUntilBlockedByAnotherBackendAsync(OpenCRMContext context, int processId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await context.Database.OpenConnectionAsync(timeout.Token);
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT cardinality(pg_blocking_pids(@processId)) > 0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "processId";
        parameter.Value = processId;
        command.Parameters.Add(parameter);

        while (!timeout.IsCancellationRequested)
        {
            var isBlocked = (bool)(await command.ExecuteScalarAsync(timeout.Token))!;
            if (isBlocked)
            {
                return;
            }

            await Task.Yield();
        }

        timeout.Token.ThrowIfCancellationRequested();
    }

    private sealed class ArchiveOrganizationLockBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _lockHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseArchive = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task WaitUntilOrganizationLockIsHeldAsync() => this._lockHeld.Task.WaitAsync(TimeSpan.FromSeconds(20));

        internal void ReleaseArchive() => this._releaseArchive.TrySetResult();

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Organizations\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
            {
                this._lockHeld.TrySetResult();
                await this._releaseArchive.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }
    }

    private sealed class OrganizationLockAttemptSignal : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _lockRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task WaitUntilOrganizationLockIsRequestedAsync() => this._lockRequested.Task.WaitAsync(TimeSpan.FromSeconds(20));

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Organizations\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
            {
                this._lockRequested.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }
    }

    private static MaintenanceTestScope CreateService(
        OpenCRMContext context,
        Vehicle vehicle,
        IVehicleCounterCommandService? vehicleService = null,
        Action? onMaintenanceLoaded = null,
        IMaintenanceConsumptionService? consumptionService = null)
    {
        var repository = new Mock<IMaintenanceRepository>();
        repository.Setup(value => value.Add(It.IsAny<MaintenanceEntity>()))
            .Callback<MaintenanceEntity>(value => context.Maintenance.Add(value)).Returns(Task.CompletedTask);
        repository.Setup(value => value.Save()).Returns(async () => { await context.SaveChangesAsync(); });
        repository.Setup(value => value.Update(It.IsAny<MaintenanceEntity>())).Callback<MaintenanceEntity>(value => context.Update(value));
        repository.Setup(value => value.GetById(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()))
            .Returns(async (string user, Guid? organization, Guid id) =>
            {
                var maintenance = await context.Maintenance.SingleOrDefaultAsync(value => value.Id == id);
                onMaintenanceLoaded?.Invoke();

                return maintenance;
            });
        repository.Setup(value => value.ExistsAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()))
            .Returns((string user, Guid? organization, Guid id) =>
                new MaintenanceRepository(context).ExistsAsync(user, organization, id));
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc));

        var maintenancePhotos = new Mock<IMaintenancePhotoRepository>();
        maintenancePhotos.Setup(value => value.GetByMaintenanceIdAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(Array.Empty<MaintenancePhoto>());
        maintenancePhotos.Setup(value => value.GetByMaintenanceIdsAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<bool>()))
            .ReturnsAsync(Array.Empty<MaintenancePhoto>());
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(value => value.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.MembersModule))
            .ReturnsAsync(true);
        var access = subscriptions.Object;
        var defaultConsumption = new Mock<IMaintenanceConsumptionService>();
        defaultConsumption.Setup(service => service.SynchronizeAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
            .ReturnsAsync(new MaintenanceConsumptionResult(0, []));
        var queryService = new MaintenanceQueryService(
            repository.Object,
            maintenancePhotos.Object,
            new VehicleMaintenanceLookupService(new VehicleRepository(context)),
            context,
            access,
            Mock.Of<OpenCRM.Modules.Clients.Contracts.IOrganizationInquiryOriginReader>());

        return new MaintenanceTestScope(repository.Object, vehicleService ?? Mock.Of<IVehicleCounterCommandService>(), clock.Object,
            context, access, queryService, new MaintenanceSlotQueryService(context, access), consumptionService ?? defaultConsumption.Object);
    }

    private static Vehicle CreateVehicle()
    {
        var user = new ApplicationUser();

        return new Vehicle
        {
            User = user,
            Organization = new Organization
            {
                Name = "Проверка связей услуг", PrimaryScenarioCode = "detailing", OwnerUser = user,
                Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
            },
            RegistrationPlate = "А123АА154"
        };
    }

    private static OrganizationScenarioCatalogItem CreateItem(Organization organization, string name)
    {
        return new OrganizationScenarioCatalogItem
        {
            Organization = organization, Name = name, KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = Guid.NewGuid().ToString("N"), ScenarioCode = "detailing", IsActive = true
        };
    }

    private static CreateMaintenanceDto CreateRequest(Guid? catalogItemId)
    {
        return new CreateMaintenanceDto
        {
            Status = MaintenanceStatus.Planned, Type = MaintenanceType.Maintenance,
            PlannedDate = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc), CompletedDate = null,
            Location = null, Performer = null, Mileage = null, Comment = null,
            Details = [new CreateMaintenanceDetailDto { CatalogItemId = catalogItemId, Name = "Подменённое название", Price = 1000, Description = null }]
        };
    }
}
