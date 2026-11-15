using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Promotions.Contracts;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

[Collection(PromotionsDatabaseCollection.Name)]
public sealed class OrganizationPromoHistoryTests(PromotionsDatabaseFixture fixture)
{
    [Fact]
    public async Task PagesHaveStableOrderAndExcludeAnotherOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var anotherOrganization = await fixture.CreateOrganizationAsync(context, organization.OwnerUserId);
        var plan = await fixture.CreatePlanAsync(context, "История промокодов");
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>();
        for (var index = 0; index < 5; index++)
        {
            var promo = await fixture.CreateGiftCodeAsync(context, plan.Id);
            await service.ApplyAsync(organization.OwnerUserId, organization.Id, promo.Code);
        }

        var anotherPromo = await fixture.CreateGiftCodeAsync(context, plan.Id);
        await service.ApplyAsync(organization.OwnerUserId, anotherOrganization.Id, anotherPromo.Code);
        var sameTime = DateTime.UtcNow;
        await context.PromoCodeApplications.Where(application => application.OrganizationId == organization.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(application => application.CreatedAt, sameTime));

        var firstPage = await service.GetApplicationsPageAsync(organization.OwnerUserId, organization.Id, 1, 3);
        var secondPage = await service.GetApplicationsPageAsync(organization.OwnerUserId, organization.Id, 2, 3);
        var allIds = firstPage.Items.Concat(secondPage.Items).Select(application => application.Id).ToArray();

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.Items.Count());
        Assert.Equal(2, secondPage.Items.Count());
        Assert.Equal(5, allIds.Distinct().Count());
        Assert.Equal(allIds.OrderDescending(), allIds);
        Assert.All(firstPage.Items.Concat(secondPage.Items), application => Assert.Equal(organization.Id, application.OrganizationId));
        var repeated = await service.GetApplicationsPageAsync(organization.OwnerUserId, organization.Id, 1, 3);
        Assert.Equal(firstPage.Items.Select(application => application.Id), repeated.Items.Select(application => application.Id));
        var lastPage = await service.GetApplicationsPageAsync(organization.OwnerUserId, organization.Id, int.MaxValue, 3);
        Assert.Equal(2, lastPage.Page);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetApplicationsPageAsync("outsider", organization.Id, 1, 20));
    }

    [Fact]
    public async Task EmptyHistoryHasBoundedPageParameters()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>();

        var page = await service.GetApplicationsPageAsync(organization.OwnerUserId, organization.Id, -2, int.MaxValue);

        Assert.Equal(1, page.Page);
        Assert.Equal(100, page.PageSize);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }
}
