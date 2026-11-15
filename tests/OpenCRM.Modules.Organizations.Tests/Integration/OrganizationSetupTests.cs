using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Setup;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationSetupTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task PostWithoutHistoricalLocationCanBeRenamed()
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationSetupTests.SeedOrganizationAsync(context);
        var post = OrganizationSetupTests.CreateCatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "Пост", "null-location");
        context.OrganizationScenarioCatalogItems.Add(post);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            Vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId },
            UserId = organization.OwnerUserId, Date = DateTime.UtcNow.Date, Location = null
        });
        await context.SaveChangesAsync();
        await OrganizationSetupTests.CreateService(context).SaveSetupAsync(organization.OwnerUserId, organization.Id,
            new SaveOrganizationSetupDto { Step = 0, Items = [new OrganizationSetupItemDto { Id = post.Id, Name = "Пост полировки" }] });

        Assert.Equal("Пост полировки", await context.OrganizationScenarioCatalogItems.AsNoTracking().Where(value => value.Id == post.Id)
            .Select(value => value.Name).SingleAsync());
    }

    [Fact]
    public async Task SetupStepsPersistCatalogScheduleRevisionAndCompletion()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var service = CreateService(context);
        var workPostId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 0,
            Revision = 0,
            Items = [new OrganizationSetupItemDto { Id = workPostId, Name = "  Полировка  " }]
        });
        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 1,
            Revision = 1,
            Items = [new OrganizationSetupItemDto
            {
                Id = serviceId,
                Name = "Полировка кузова",
                UnitPrice = 1250,
                DurationMinutes = 90
            }]
        });
        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 2,
            Revision = 2,
            WorkdayStart = new TimeOnly(9, 0),
            WorkdayEnd = new TimeOnly(19, 0),
            WorkingDays = [1, 2, 3, 4, 5]
        });
        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 3,
            Revision = 3
        });

        context.ChangeTracker.Clear();
        var result = await service.GetSetupAsync(organization.OwnerUserId, organization.Id);

        Assert.Equal(4, result.Revision);
        Assert.Equal(3, result.Step);
        Assert.NotNull(result.CompletedAt);
        Assert.Equal(new TimeOnly(9, 0), result.WorkdayStart);
        Assert.Equal(new TimeOnly(19, 0), result.WorkdayEnd);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result.WorkingDays);
        Assert.Equal("Полировка", await context.OrganizationScenarioCatalogItems
            .Where(item => item.Id == workPostId)
            .Select(item => item.Name)
            .SingleAsync());
        var savedService = await context.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == serviceId);
        Assert.Equal(1250, savedService.UnitPrice);
        Assert.Equal(90, savedService.DurationMinutes);
    }

    [Fact]
    public async Task CompletingSetupAgainPreservesOriginalCompletionTime()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var service = CreateService(context);
        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id,
            new SaveOrganizationSetupDto { Step = 3, Revision = 0 });
        var completedAt = await context.Organizations
            .Where(value => value.Id == organization.Id)
            .Select(value => value.SetupCompletedAt)
            .SingleAsync();
        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id,
            new SaveOrganizationSetupDto { Step = 3, Revision = 1 });

        Assert.Equal(completedAt, await context.Organizations
            .Where(value => value.Id == organization.Id)
            .Select(value => value.SetupCompletedAt)
            .SingleAsync());
        Assert.Equal(2, await context.Organizations
            .Where(value => value.Id == organization.Id)
            .Select(value => value.SetupRevision)
            .SingleAsync());
    }

    [Fact]
    public async Task ExistingUnusedCatalogItemsCanBeRenamedAndSaved()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var workPost = CreateCatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "Пост", "existing-post");
        var serviceItem = CreateCatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Работа", "existing-service");
        context.OrganizationScenarioCatalogItems.AddRange(workPost, serviceItem);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 0,
            Items = [new OrganizationSetupItemDto { Id = workPost.Id, Name = "Обновлённый пост" }]
        });
        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 1,
            Revision = 1,
            Items = [new OrganizationSetupItemDto
            {
                Id = serviceItem.Id,
                Name = "Обновлённая работа",
                UnitPrice = 500,
                DurationMinutes = 60
            }]
        });

        context.ChangeTracker.Clear();
        var updatedItems = await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organization.Id)
            .OrderBy(item => item.KindCode)
            .ToListAsync();
        Assert.Equal("Обновлённый пост", Assert.Single(updatedItems, item => item.Id == workPost.Id).Name);
        var updatedService = Assert.Single(updatedItems, item => item.Id == serviceItem.Id);
        Assert.Equal("Обновлённая работа", updatedService.Name);
        Assert.Equal(500, updatedService.UnitPrice);
        Assert.Equal(60, updatedService.DurationMinutes);
    }

    [Fact]
    public async Task InactiveExistingCatalogItemDoesNotReserveItsName()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var inactiveItem = CreateCatalogItem(
            organization,
            OrganizationScenarioCatalogKindCodes.WorkPost,
            "Повторяемое имя",
            "inactive-post");
        inactiveItem.IsActive = false;
        context.OrganizationScenarioCatalogItems.Add(inactiveItem);
        await context.SaveChangesAsync();

        var newItemId = Guid.NewGuid();
        await CreateService(context).SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 0,
            Items = [new OrganizationSetupItemDto { Id = newItemId, Name = inactiveItem.Name }]
        });

        Assert.True(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == newItemId && item.IsActive));
    }

    [Fact]
    public async Task NewInactiveCatalogItemIsSkipped()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var itemId = Guid.NewGuid();

        await CreateService(context).SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 0,
            Items = [new OrganizationSetupItemDto { Id = itemId, Name = "Отключенный пост", IsActive = false }]
        });

        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == itemId));
        Assert.Equal(1, await context.Organizations
            .Where(item => item.Id == organization.Id)
            .Select(item => item.SetupRevision)
            .SingleAsync());
    }

    [Fact]
    public async Task UnusedPostCanBeRenamedWhenMaintenanceUsesAnotherPost()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var post = CreateCatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "Свободный пост", "free-post");
        var vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId };
        context.OrganizationScenarioCatalogItems.Add(post);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = organization.OwnerUserId,
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow.Date,
            PlannedDate = DateTime.UtcNow.Date,
            Location = "Занятый пост"
        });
        await context.SaveChangesAsync();

        await CreateService(context).SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 0,
            Items = [new OrganizationSetupItemDto { Id = post.Id, Name = "Переименованный пост" }]
        });

        Assert.Equal("Переименованный пост", await context.OrganizationScenarioCatalogItems
            .Where(item => item.Id == post.Id)
            .Select(item => item.Name)
            .SingleAsync());
    }

    [Fact]
    public async Task GetSetupUsesLegacyScheduleWhenNoDetailedRowsExist()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        organization.WorkingDays = [1, 4];
        organization.WorkdayStart = new TimeOnly(8, 0);
        organization.WorkdayEnd = new TimeOnly(17, 30);
        organization.SetupAutoOpenPending = true;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var result = await service.GetSetupAsync(organization.OwnerUserId, organization.Id);

        Assert.True(result.ShouldAutoOpen);
        Assert.Equal(0, result.Step);
        Assert.Equal(0, result.Revision);
        Assert.True(result.CanManage);
        Assert.Equal(new[] { 1, 4 }, result.WorkingDays);
        Assert.Equal(new TimeOnly(8, 0), result.WeeklySchedule[4].StartTime);
        Assert.Empty(await context.OrganizationWorkingDays
            .Where(day => day.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task GetSetupFallsBackToLegacyHoursWhenNoDayIsMarkedAsWorking()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var result = await CreateService(context).GetSetupAsync(organization.OwnerUserId, organization.Id);

        Assert.Empty(result.WorkingDays);
        Assert.Null(result.WorkdayStart);
        Assert.Null(result.WorkdayEnd);
        Assert.Equal(7, result.WeeklySchedule.Count);
        Assert.All(result.WeeklySchedule, day => Assert.False(day.IsWorking));
    }

    [Fact]
    public async Task SaveDetailedSchedulePersistsBreaksAndDerivedLegacyFields()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var service = CreateService(context);
        var schedule = ClosedSchedule();
        schedule[1] = OpenDay(1, new TimeOnly(8, 0), new TimeOnly(18, 0),
        [
            new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(14, 30) },
            new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(13, 0) }
        ]);
        schedule[5] = OpenDay(5, new TimeOnly(10, 0), new TimeOnly(20, 0));

        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 2,
            WeeklySchedule = schedule
        });

        context.ChangeTracker.Clear();
        var result = await service.GetSetupAsync(organization.OwnerUserId, organization.Id);
        Assert.Equal(new[] { 1, 5 }, result.WorkingDays);
        Assert.Equal(new TimeOnly(8, 0), result.WorkdayStart);
        Assert.Equal(new TimeOnly(20, 0), result.WorkdayEnd);
        Assert.Collection(result.WeeklySchedule[1].Breaks,
            value => Assert.Equal(new TimeOnly(12, 0), value.StartTime),
            value => Assert.Equal(new TimeOnly(14, 0), value.StartTime));
        Assert.Equal(2, await context.OrganizationWorkingDayBreaks.CountAsync());
    }

    [Fact]
    public async Task UpdatingDetailedScheduleReconcilesDaysAndBreaks()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        context.OrganizationWorkingDays.AddRange(
            new OrganizationWorkingDay
            {
                OrganizationId = organization.Id,
                DayOfWeek = 1,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(18, 0),
                Breaks =
                [
                    new OrganizationWorkingDayBreak
                    {
                        OrganizationId = organization.Id,
                        DayOfWeek = 1,
                        StartTime = new TimeOnly(12, 0),
                        EndTime = new TimeOnly(13, 0)
                    },
                    new OrganizationWorkingDayBreak
                    {
                        OrganizationId = organization.Id,
                        DayOfWeek = 1,
                        StartTime = new TimeOnly(15, 0),
                        EndTime = new TimeOnly(15, 30)
                    }
                ]
            },
            new OrganizationWorkingDay
            {
                OrganizationId = organization.Id,
                DayOfWeek = 2,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(18, 0)
            });
        await context.SaveChangesAsync();
        var schedule = ClosedSchedule();
        schedule[1] = OpenDay(1, new TimeOnly(8, 0), new TimeOnly(19, 0),
        [
            new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(12, 45) },
            new OrganizationWorkingDayBreakDto { StartTime = new TimeOnly(16, 0), EndTime = new TimeOnly(16, 30) }
        ]);
        schedule[4] = OpenDay(4, new TimeOnly(10, 0), new TimeOnly(17, 0));

        await CreateService(context).SaveSetupAsync(organization.OwnerUserId, organization.Id,
            new SaveOrganizationSetupDto { Step = 2, WeeklySchedule = schedule });

        var days = await context.OrganizationWorkingDays
            .Include(day => day.Breaks)
            .Where(day => day.OrganizationId == organization.Id)
            .OrderBy(day => day.DayOfWeek)
            .ToListAsync();
        Assert.Equal(new[] { 1, 4 }, days.Select(day => day.DayOfWeek));
        Assert.Equal(new TimeOnly(8, 0), days[0].StartTime);
        Assert.Collection(days[0].Breaks.OrderBy(value => value.StartTime),
            value => Assert.Equal(new TimeOnly(12, 45), value.EndTime),
            value => Assert.Equal(new TimeOnly(16, 30), value.EndTime));
        Assert.Empty(days[1].Breaks);
        Assert.Equal(new TimeOnly(10, 0), days[1].StartTime);
    }

    [Fact]
    public async Task LegacyPayloadCannotReplaceAnExistingDetailedSchedule()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        context.OrganizationWorkingDays.Add(new OrganizationWorkingDay
        {
            OrganizationId = organization.Id,
            DayOfWeek = 1,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(18, 0),
            Breaks =
            [
                new OrganizationWorkingDayBreak
                {
                    OrganizationId = organization.Id,
                    DayOfWeek = 1,
                    StartTime = new TimeOnly(12, 0),
                    EndTime = new TimeOnly(13, 0)
                }
            ]
        });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Step = 2,
                WorkdayStart = new TimeOnly(9, 0),
                WorkdayEnd = new TimeOnly(18, 0),
                WorkingDays = [1]
            }));

        Assert.Equal(0, await context.Organizations
            .Where(value => value.Id == organization.Id)
            .Select(value => value.SetupRevision)
            .SingleAsync());
        Assert.Single(await context.OrganizationWorkingDayBreaks
            .Where(value => value.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task StaleSetupRevisionIsRejectedAndConcurrentCommandsDoNotDuplicateItems()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var dto = new SaveOrganizationSetupDto
        {
            Step = 0,
            Items = [new OrganizationSetupItemDto { Id = Guid.NewGuid(), Name = "Пост" }]
        };

        async Task SaveAsync()
        {
            await using var attempt = fixture.CreateContext();
            await CreateService(attempt).SaveSetupAsync(organization.OwnerUserId, organization.Id, dto);
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await SaveAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }));

        Assert.Single(outcomes, value => value);
        Assert.Single(await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(SaveAsync);
    }

    [Fact]
    public async Task SetupWritesJoinAnAmbientTransactionAndRollbackWithIt()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var itemId = Guid.NewGuid();
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await CreateService(context).SaveSetupAsync(organization.OwnerUserId, organization.Id,
                new SaveOrganizationSetupDto
                {
                    Step = 0,
                    Items = [new OrganizationSetupItemDto { Id = itemId, Name = "Временный пост" }]
                });
            Assert.True(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == itemId));
            await transaction.RollbackAsync();
        });

        context.ChangeTracker.Clear();
        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == itemId));
        Assert.Equal(0, await context.Organizations
            .Where(value => value.Id == organization.Id)
            .Select(value => value.SetupRevision)
            .SingleAsync());
    }

    [Fact]
    public async Task InvalidSetupHeaderAndItemValuesAreRejectedBeforeWriting()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var service = CreateService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto { Step = -1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto { Step = 4 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto { Items = null! }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Items = Enumerable.Range(0, 101)
                    .Select(index => new OrganizationSetupItemDto { Id = Guid.NewGuid(), Name = $"Позиция {index}" })
                    .ToList()
            }));

        var invalidItems = new List<OrganizationSetupItemDto>
        {
            new() { Id = Guid.Empty, Name = "Пост" },
            new() { Id = Guid.NewGuid(), Name = " " },
            new() { Id = Guid.NewGuid(), Name = new string('А', 161) },
            new() { Id = Guid.NewGuid(), Name = "Платная работа", UnitPrice = -1 },
            new() { Id = Guid.NewGuid(), Name = "Платная работа", UnitPrice = 1000000000 },
            new() { Id = Guid.NewGuid(), Name = "Платная работа", UnitPrice = 1.234m },
            new() { Id = Guid.NewGuid(), Name = "Длительная работа", DurationMinutes = 0 },
            new() { Id = Guid.NewGuid(), Name = "Длительная работа", DurationMinutes = 525601 }
        };

        foreach (var item in invalidItems)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
                organization.OwnerUserId,
                organization.Id,
                new SaveOrganizationSetupDto { Items = [item] }));
        }

        var duplicateId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Items =
                [
                    new OrganizationSetupItemDto { Id = duplicateId, Name = "Пост 1" },
                    new OrganizationSetupItemDto { Id = duplicateId, Name = "Пост 2" }
                ]
            }));
        Assert.Empty(await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task DuplicateActiveNamesAndForeignOrWrongKindIdsAreRejected()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var otherOrganization = await SeedOrganizationAsync(context);
        var foreignItem = new OrganizationScenarioCatalogItem
        {
            OrganizationId = otherOrganization.Id,
            ScenarioCode = otherOrganization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
            Code = "foreign-post",
            Name = "Чужой пост"
        };
        var wrongKindItem = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = "service-as-post",
            Name = "Услуга"
        };
        var existingItem = CreateCatalogItem(
            organization,
            OrganizationScenarioCatalogKindCodes.WorkPost,
            "Повторяемое имя",
            "existing-post");
        context.OrganizationScenarioCatalogItems.AddRange(foreignItem, wrongKindItem, existingItem);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto { Items = [new OrganizationSetupItemDto { Id = foreignItem.Id, Name = "Чужой пост" }] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto { Items = [new OrganizationSetupItemDto { Id = wrongKindItem.Id, Name = "Услуга" }] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Items =
                [
                    new OrganizationSetupItemDto { Id = Guid.NewGuid(), Name = "Одинаковое" },
                    new OrganizationSetupItemDto { Id = Guid.NewGuid(), Name = " одинаковое " }
                ]
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Items = [new OrganizationSetupItemDto { Id = Guid.NewGuid(), Name = "Повторяемое имя" }]
            }));
    }

    [Fact]
    public async Task UsedPostAndServiceCannotBeDisabledAndFailedBatchRollsBack()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var post = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
            Code = "used-post",
            Name = "Пост 1"
        };
        var item = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = "used-service",
            Name = "Полировка"
        };
        var bundledItem = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = "bundled-service",
            Name = "Мойка"
        };
        var bundle = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle,
            Code = "used-bundle",
            Name = "Комплекс",
            BundleItems = [new OrganizationServiceBundleItem
            {
                OrganizationId = organization.Id,
                ServiceId = bundledItem.Id,
                Position = 0
            }]
        };
        var vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId };
        context.OrganizationScenarioCatalogItems.AddRange(post, item, bundledItem, bundle);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = organization.OwnerUserId,
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow.Date,
            PlannedDate = DateTime.UtcNow.Date,
            Location = post.Name,
            Details = [new MaintenanceDetail { Name = item.Name, CatalogItemId = item.Id, Price = 500 }]
        });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Step = 0,
                Items =
                [
                    new OrganizationSetupItemDto { Id = Guid.NewGuid(), Name = "Новый пост" },
                    new OrganizationSetupItemDto { Id = post.Id, Name = post.Name, IsActive = false }
                ]
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Step = 0,
                Items = [new OrganizationSetupItemDto { Id = post.Id, Name = "Переименованный пост" }]
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Step = 1,
                Items = [new OrganizationSetupItemDto { Id = item.Id, Name = item.Name, IsActive = false }]
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto
            {
                Step = 1,
                Items = [new OrganizationSetupItemDto { Id = bundledItem.Id, Name = bundledItem.Name, IsActive = false }]
            }));

        context.ChangeTracker.Clear();
        Assert.Equal(1, await context.OrganizationScenarioCatalogItems.CountAsync(value => value.Id == post.Id && value.IsActive));
        Assert.Equal(1, await context.OrganizationScenarioCatalogItems.CountAsync(value => value.Id == item.Id && value.IsActive));
        Assert.Equal(0, await context.Organizations
            .Where(value => value.Id == organization.Id)
            .Select(value => value.SetupRevision)
            .SingleAsync());
    }

    [Fact]
    public async Task UsedServiceCanBeRenamedWhileRemainingActive()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var item = CreateCatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Работа", "used-service");
        var vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId };
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = organization.OwnerUserId,
            Status = MaintenanceStatus.Completed,
            Date = DateTime.UtcNow.Date,
            CompletedDate = DateTime.UtcNow,
            Details = [new MaintenanceDetail { Name = item.Name, CatalogItemId = item.Id, Price = 500 }]
        });
        await context.SaveChangesAsync();

        await CreateService(context).SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 1,
            Items = [new OrganizationSetupItemDto { Id = item.Id, Name = "Новое название" }]
        });

        context.ChangeTracker.Clear();
        var saved = await context.OrganizationScenarioCatalogItems.SingleAsync(value => value.Id == item.Id);
        Assert.Equal("Новое название", saved.Name);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task UsedInactiveServiceCanRemainInactiveDuringSetup()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var item = CreateCatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Работа", "inactive-used-service");
        item.IsActive = false;
        var vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId };
        context.OrganizationScenarioCatalogItems.Add(item);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = organization.OwnerUserId,
            Status = MaintenanceStatus.Completed,
            Date = DateTime.UtcNow.Date,
            CompletedDate = DateTime.UtcNow,
            Details = [new MaintenanceDetail { Name = item.Name, CatalogItemId = item.Id, Price = 500 }]
        });
        await context.SaveChangesAsync();

        await CreateService(context).SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 1,
            Items = [new OrganizationSetupItemDto { Id = item.Id, Name = item.Name, IsActive = false }]
        });

        Assert.False(await context.OrganizationScenarioCatalogItems
            .Where(value => value.Id == item.Id)
            .Select(value => value.IsActive)
            .SingleAsync());
    }

    [Fact]
    public async Task InactiveNewItemsAreIgnoredAndSkipAdvancesRevisionWithoutCatalogChanges()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var service = CreateService(context);
        var ignoredId = Guid.NewGuid();
        await service.SaveSetupAsync(organization.OwnerUserId, organization.Id, new SaveOrganizationSetupDto
        {
            Step = 0,
            Skip = true,
            Items = [new OrganizationSetupItemDto { Id = ignoredId, Name = "Отключенный", IsActive = false }]
        });

        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == ignoredId));
        var setup = await service.GetSetupAsync(organization.OwnerUserId, organization.Id);
        Assert.Equal(1, setup.Revision);
        Assert.Equal(1, setup.Step);
        Assert.False(setup.ShouldAutoOpen);
    }

    [Fact]
    public async Task OpeningSetupIsSingleUseAndSavingClosesIt()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        organization.SetupAutoOpenPending = true;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Assert.True(await service.TryOpenSetupAsync(organization.OwnerUserId, organization.Id));
        Assert.False(await service.TryOpenSetupAsync(organization.OwnerUserId, organization.Id));
        Assert.False((await service.GetSetupAsync(organization.OwnerUserId, organization.Id)).ShouldAutoOpen);

        organization.SetupAutoOpenPending = true;
        organization.SetupCompletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        Assert.False(await service.TryOpenSetupAsync(organization.OwnerUserId, organization.Id));
        Assert.False((await service.GetSetupAsync(organization.OwnerUserId, organization.Id)).ShouldAutoOpen);

        organization.SetupCompletedAt = null;
        organization.IsDeleted = true;
        await context.SaveChangesAsync();
        Assert.False(await service.TryOpenSetupAsync(organization.OwnerUserId, organization.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSetupAsync(
            organization.OwnerUserId,
            organization.Id));
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    public async Task TryOpenReturnsFalseWhenOrganizationIsUnavailable(string state)
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        organization.SetupAutoOpenPending = true;
        switch (state)
        {
            case "archived":
                organization.Archive(DateTime.UtcNow);
                break;
            case "inactive":
                organization.IsActive = false;
                break;
            case "deleted":
                organization.Delete(DateTime.UtcNow);
                break;
        }

        await context.SaveChangesAsync();

        Assert.False(await CreateService(context).TryOpenSetupAsync(organization.OwnerUserId, organization.Id));
    }

    [Fact]
    public async Task TryOpenReturnsFalseWhenOrganizationDoesNotExist()
    {
        await using var context = fixture.CreateContext();

        Assert.False(await CreateService(context).TryOpenSetupAsync("missing-user", Guid.NewGuid()));
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    public async Task SaveRejectsOrganizationsThatAreNoLongerAvailable(string state)
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        switch (state)
        {
            case "archived":
                organization.Archive(DateTime.UtcNow);
                break;
            case "inactive":
                organization.IsActive = false;
                break;
            case "deleted":
                organization.Delete(DateTime.UtcNow);
                break;
        }

        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(context).SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto()));
    }

    [Fact]
    public async Task ReadPermissionAndWritePermissionAreRequired()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var deniedRead = CreateSubscription();
        deniedRead.Setup(value => value.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Нет доступа к чтению."));
        var readService = CreateService(context, deniedRead.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => readService.GetSetupAsync(
            organization.OwnerUserId,
            organization.Id));

        var deniedWrite = CreateSubscription();
        deniedWrite.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Нет доступа к изменению."));
        var writeService = CreateService(context, deniedWrite.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writeService.TryOpenSetupAsync(
            organization.OwnerUserId,
            organization.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writeService.SaveSetupAsync(
            organization.OwnerUserId,
            organization.Id,
            new SaveOrganizationSetupDto()));
        Assert.Equal(0, (await context.Organizations.SingleAsync(value => value.Id == organization.Id)).SetupRevision);
    }

    [Fact]
    public async Task GetSetupReflectsManagePermissionWithoutBlockingRead()
    {
        await using var context = fixture.CreateContext();
        var organization = await SeedOrganizationAsync(context);
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(value => value.HasPermissionAsync(
                organization.OwnerUserId,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .ReturnsAsync(false);

        var result = await CreateService(context, permissions: permissions.Object)
            .GetSetupAsync(organization.OwnerUserId, organization.Id);

        Assert.False(result.CanManage);
    }

    private static OrganizationSetupService CreateService(
        OpenCRMContext context,
        IOrganizationSubscriptionService? subscription = null,
        IOrganizationPermissionService? permissions = null)
    {
        using var services = new ServiceCollection().AddSingleton(context)
            .AddSingleton(subscription ?? CreateSubscription().Object).AddOpenCRMCatalog().BuildServiceProvider();

        return new OrganizationSetupService(
            context,
            subscription ?? CreateSubscription().Object,
            permissions ?? CreatePermissions().Object,
            services.GetRequiredService<ICatalogSetupService>(),
            new OrganizationWorkingSchedule(context),
            new OrganizationTransaction(context));
    }

    private static Mock<IOrganizationSubscriptionService> CreateSubscription()
    {
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(value => value.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return subscription;
    }

    private static Mock<IOrganizationPermissionService> CreatePermissions()
    {
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(value => value.HasPermissionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        return permissions;
    }

    private static List<OrganizationWorkingDayDto> ClosedSchedule() => Enumerable.Range(0, 7)
        .Select(day => new OrganizationWorkingDayDto { DayOfWeek = day })
        .ToList();

    private static OrganizationScenarioCatalogItem CreateCatalogItem(
        Organization organization,
        string kindCode,
        string name,
        string code) => new()
        {
            OrganizationId = organization.Id,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = kindCode,
            Code = code,
            Name = name
        };

    private static OrganizationWorkingDayDto OpenDay(
        int dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        List<OrganizationWorkingDayBreakDto>? breaks = null) => new()
        {
            DayOfWeek = dayOfWeek,
            IsWorking = true,
            StartTime = startTime,
            EndTime = endTime,
            Breaks = breaks ?? []
        };

    private static async Task<Organization> SeedOrganizationAsync(OpenCRMContext context)
    {
        var organization = new Organization
        {
            Name = "Настройка студии",
            PrimaryScenarioCode = "detailing",
            OwnerUser = new ApplicationUser { Email = $"setup-{Guid.NewGuid():N}@example.test" }
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }
}
