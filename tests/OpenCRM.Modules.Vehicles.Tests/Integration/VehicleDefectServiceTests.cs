using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Defects;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleDefectServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task ListPreservesSearchPagingOrderAndOrganizationIsolation()
    {
        var seed = await this.SeedOrganizationVehicleAsync();
        var foreignSeed = await this.SeedOrganizationVehicleAsync();
        await using var context = fixture.CreateContext();
        var start = DateTime.UtcNow.AddDays(-2);
        var first = new VehicleDefect
        {
            OrganizationId = seed.Organization.Id, VehicleId = seed.Vehicle.Id, UserId = seed.User.Id,
            Title = "Течь масла", CreatedAt = start, UpdatedAt = start
        };
        var second = new VehicleDefect
        {
            OrganizationId = seed.Organization.Id, VehicleId = seed.Vehicle.Id, UserId = seed.User.Id,
            Title = "Течь охлаждения", StatusCode = VehicleDefectStatusCodes.Resolved,
            CreatedAt = start.AddHours(1), UpdatedAt = start.AddHours(1)
        };
        context.VehicleDefects.AddRange(first, second,
            new VehicleDefect
            {
                OrganizationId = seed.Organization.Id, VehicleId = seed.Vehicle.Id, UserId = seed.User.Id,
                Title = "Течь в удалённой записи", IsDeleted = true, CreatedAt = start, UpdatedAt = start
            },
            new VehicleDefect
            {
                OrganizationId = foreignSeed.Organization.Id, VehicleId = foreignSeed.Vehicle.Id, UserId = foreignSeed.User.Id,
                Title = "Чужая течь", CreatedAt = start, UpdatedAt = start
            });
        await context.SaveChangesAsync();
        var service = this.CreateService(context);

        var page = await service.GetAsync(seed.User.Id, seed.Organization.Id, 1, 1, "тЕчЬ", sort: "created_asc");
        var nextPage = await service.GetAsync(seed.User.Id, seed.Organization.Id, 2, 1, "тЕчЬ", sort: "created_asc");

        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Equal(second.Id, Assert.Single(nextPage.Items).Id);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.OpenCount);
        Assert.Equal(1, page.ResolvedCount);
        Assert.True(page.HasMore);
        Assert.False(nextPage.HasMore);
    }

    [Fact]
    public async Task DefectLifecycleWritesHistoryAndSoftDeletesRecord()
    {
        var seed = await this.SeedOrganizationVehicleAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context);

        var defectId = await service.CreateAsync(seed.User.Id, seed.Organization.Id, new CreateVehicleDefectDto
        {
            VehicleId = seed.Vehicle.Id,
            Title = "  Течь масла  ",
            Description = "  Проверить двигатель  ",
            SeverityCode = VehicleDefectSeverityCodes.High
        });

        await service.UpdateAsync(seed.User.Id, seed.Organization.Id, defectId, new UpdateVehicleDefectDto
        {
            VehicleId = seed.Vehicle.Id,
            Title = "Течь масла устранена",
            Description = "Заменить прокладку",
            SeverityCode = VehicleDefectSeverityCodes.Critical,
            StatusCode = VehicleDefectStatusCodes.InProgress
        });
        await service.UpdateStatusAsync(seed.User.Id, seed.Organization.Id, defectId, VehicleDefectStatusCodes.Resolved);

        var defect = await service.GetByIdAsync(seed.User.Id, seed.Organization.Id, defectId);
        var history = await service.GetHistoryAsync(seed.User.Id, seed.Organization.Id, defectId);

        Assert.NotNull(defect);
        Assert.Equal("Течь масла устранена", defect.Title);
        Assert.Equal(VehicleDefectSeverityCodes.Critical, defect.SeverityCode);
        Assert.Equal(VehicleDefectStatusCodes.Resolved, defect.StatusCode);
        Assert.True(defect.IsClosed);
        Assert.Contains(history, item => item.Title == "Создана неисправность");
        Assert.Contains(history, item => item.Title == "Изменена неисправность");
        Assert.Contains(history, item => item.Title == "Изменено состояние неисправности");
        Assert.All(history, item => Assert.Equal(seed.User.Email, item.ActorDisplayName));

        await service.DeleteAsync(seed.User.Id, seed.Organization.Id, defectId);

        Assert.Null(await service.GetByIdAsync(seed.User.Id, seed.Organization.Id, defectId));
        Assert.Equal(4, (await service.GetHistoryAsync(seed.User.Id, seed.Organization.Id, defectId)).Count);
        Assert.True(await context.VehicleDefects.AnyAsync(item => item.Id == defectId && item.IsDeleted));
    }

    [Fact]
    public async Task DefectCannotBeCreatedForVehicleFromAnotherOrganization()
    {
        var seed = await this.SeedOrganizationVehicleAsync();
        var otherOrganization = await this.SeedOrganizationVehicleAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            seed.User.Id,
            seed.Organization.Id,
            new CreateVehicleDefectDto
            {
                VehicleId = otherOrganization.Vehicle.Id,
                Title = "Чужая техника"
            }));

        Assert.Equal("Техника для неисправности не найдена.", exception.Message);
        Assert.False(await context.VehicleDefects.AnyAsync(item => item.OrganizationId == seed.Organization.Id));
    }

    [Fact]
    public async Task DefectAndHistoryAreRolledBackWithOwningTransaction()
    {
        var seed = await this.SeedOrganizationVehicleAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context);
        var defectId = Guid.Empty;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            defectId = await service.CreateAsync(seed.User.Id, seed.Organization.Id, new CreateVehicleDefectDto
            {
                VehicleId = seed.Vehicle.Id,
                Title = "Откатываемая неисправность"
            });
            await transaction.RollbackAsync();
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.VehicleDefects.AnyAsync(item => item.Id == defectId));
        Assert.False(await verificationContext.VehicleHistoryEvents.AnyAsync(item => item.RelatedEntityId == defectId));
    }

    private async Task<(ApplicationUser User, Organization Organization, Vehicle Vehicle)> SeedOrganizationVehicleAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = $"defects-{Guid.NewGuid():N}@example.test",
            UserName = $"defects-{Guid.NewGuid():N}"
        };
        var organization = new Organization { Name = "Организация неисправностей", OwnerUser = user, OwnerUserId = user.Id };
        var vehicle = new Vehicle
        {
            User = user,
            UserId = user.Id,
            Organization = organization,
            OrganizationId = organization.Id,
            Name = "Тестовая техника",
            DateTimeCreation = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return (user, organization, vehicle);
    }

    private VehicleDefectService CreateService(OpenCRMContext context)
    {
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription
            .Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.DefectsModule))
            .ReturnsAsync(true);
        subscription
            .Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription
            .Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return new VehicleDefectService(context, subscription.Object);
    }
}
