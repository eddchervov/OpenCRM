using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class OrganizationInquiryOriginReaderTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task FindsExistingInquiryAssociationWithoutReturningMutableContactData()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var maintenance = await OrganizationInquiryOriginReaderTests.CreateMaintenanceAsync(context, fixture, organization);
        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Контакт до редактирования",
            Interest = "Детейлинг",
            MaintenanceId = maintenance.Id,
            CreatedByUserId = organization.OwnerUserId
        };
        context.OrganizationInquiries.Add(inquiry);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var reader = services.GetRequiredService<IOrganizationInquiryOriginReader>();

        var source = await reader.FindSourceAsync(organization.OwnerUserId, organization.Id, maintenance.Id);

        Assert.Equal(inquiry.Id, source?.InquiryId);
        var anotherMaintenance = await OrganizationInquiryOriginReaderTests.CreateMaintenanceAsync(context, fixture, organization);
        Assert.Null(await reader.FindSourceAsync(organization.OwnerUserId, organization.Id, anotherMaintenance.Id));
    }

    [Fact]
    public async Task HidesSourceWhenScenarioOrInquiryPermissionIsUnavailable()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var maintenance = await OrganizationInquiryOriginReaderTests.CreateMaintenanceAsync(context, fixture, organization);
        var inquiry = new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = "Контакт",
            Interest = "Услуга",
            MaintenanceId = maintenance.Id,
            CreatedByUserId = organization.OwnerUserId
        };
        context.OrganizationInquiries.Add(inquiry);
        await context.SaveChangesAsync();

        organization.PrimaryScenarioCode = OrganizationTypeCodes.Fleet;
        await context.SaveChangesAsync();
        using (var hiddenScenarioServices = fixture.CreateServices(context))
        {
            var hiddenScenarioReader = hiddenScenarioServices.GetRequiredService<IOrganizationInquiryOriginReader>();
            Assert.Null(await hiddenScenarioReader.FindSourceAsync(organization.OwnerUserId, organization.Id, maintenance.Id));
        }

        organization.PrimaryScenarioCode = OrganizationTypeCodes.Detailing;
        await context.SaveChangesAsync();
        using var restrictedServices = fixture.CreateServices(
            context,
            deniedPermissionCodes: new HashSet<string> { OrganizationPermissionCodes.InquiriesView });
        var restrictedReader = restrictedServices.GetRequiredService<IOrganizationInquiryOriginReader>();

        Assert.Null(await restrictedReader.FindSourceAsync(organization.OwnerUserId, organization.Id, maintenance.Id));

        var disabledFeature = new Mock<IOrganizationSubscriptionService>();
        disabledFeature.Setup(service => service.HasFeatureAsync(organization.OwnerUserId, organization.Id, Helpers.FeatureCodes.ClientsModule))
            .ReturnsAsync(false);
        using var disabledFeatureServices = fixture.CreateServices(context, subscriptionService: disabledFeature.Object);
        var disabledFeatureReader = disabledFeatureServices.GetRequiredService<IOrganizationInquiryOriginReader>();
        Assert.Null(await disabledFeatureReader.FindSourceAsync(organization.OwnerUserId, organization.Id, maintenance.Id));
        Assert.True(await context.OrganizationInquiries.AsNoTracking().AnyAsync(item => item.Id == inquiry.Id));
    }

    private static async Task<OpenCRM.Domain.Maintenance> CreateMaintenanceAsync(
        OpenCRMContext context,
        ClientsDatabaseFixture fixture,
        Organization organization)
    {
        var vehicle = await fixture.AddVehicleAsync(context, organization);
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = organization.OwnerUser,
            Vehicle = vehicle,
            Type = OpenCRM.Domain.Enums.MaintenanceType.Maintenance,
            Status = OpenCRM.Domain.Enums.MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        return maintenance;
    }
}
