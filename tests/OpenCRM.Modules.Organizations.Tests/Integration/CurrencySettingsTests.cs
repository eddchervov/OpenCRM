using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class CurrencySettingsTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task EmptyOrganizationCanChangeCurrencyAndRejectsStaleUpdates()
    {
        await using var context = fixture.CreateContext();
        var organization = new Organization
        {
            Name = "Тестовая валюта",
            OwnerUser = new ApplicationUser(),
            IsActive = true
        };
        organization.Members.Add(new OrganizationMember
        {
            User = organization.OwnerUser,
            RoleCode = OrganizationRoleCodes.Owner
        });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var service = CurrencySettingsTests.CreateService(context);

        var result = await service.UpdateAsync(organization.OwnerUserId, organization.Id, new UpdateCurrencySettingsDto
        {
            CurrencyCode = "cny",
            ExpectedCurrencyCode = "RUB"
        });

        Assert.Equal("CNY", result.CurrencyCode);
        Assert.True(result.CanChange);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.UpdateAsync(
            organization.OwnerUserId, organization.Id, new UpdateCurrencySettingsDto
            {
                CurrencyCode = "USD",
                ExpectedCurrencyCode = "RUB"
            }));
        Assert.Equal("CNY", await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organization.Id).Select(item => item.CurrencyCode).SingleAsync());
    }

    [Fact]
    public async Task InactiveCatalogWithoutPriceStillLocksCurrency()
    {
        await using var context = fixture.CreateContext();
        var organization = new Organization
        {
            Name = "Тестовый каталог",
            OwnerUser = new ApplicationUser(),
            IsActive = true
        };
        organization.Members.Add(new OrganizationMember
        {
            User = organization.OwnerUser,
            RoleCode = OrganizationRoleCodes.Owner
        });
        context.OrganizationScenarioCatalogItems.Add(new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            Code = "currency-test",
            Name = "Тестовая позиция",
            IsActive = false
        });
        await context.SaveChangesAsync();
        var service = CurrencySettingsTests.CreateService(context);

        Assert.False((await service.GetAsync(organization.OwnerUserId, organization.Id)).CanChange);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            organization.OwnerUserId, organization.Id, new UpdateCurrencySettingsDto
            {
                CurrencyCode = "USD",
                ExpectedCurrencyCode = "RUB"
            }));
        Assert.Equal("RUB", await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organization.Id).Select(item => item.CurrencyCode).SingleAsync());
    }

    [Fact]
    public async Task PersonalCurrencyRejectsUnsupportedAndStaleValues()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new UserCurrencyService(context);

        Assert.Equal("EUR", (await service.UpdateAsync(user.Id, new UpdateCurrencySettingsDto
        {
            CurrencyCode = "EUR",
            ExpectedCurrencyCode = "RUB"
        })).CurrencyCode);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.UpdateAsync(user.Id, new UpdateCurrencySettingsDto
        {
            CurrencyCode = "USD",
            ExpectedCurrencyCode = "RUB"
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(user.Id, new UpdateCurrencySettingsDto
        {
            CurrencyCode = "JPY",
            ExpectedCurrencyCode = "EUR"
        }));
    }

    private static OrganizationCurrencyService CreateService(OpenCRMContext context)
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), OrganizationPermissionCodes.WorkspaceProfileEdit)).ReturnsAsync(true);

        return new OrganizationCurrencyService(
            context,
            subscriptions.Object,
            permissions.Object,
            new OrganizationAuditService(context),
            new OrganizationTransaction(context));
    }
}
