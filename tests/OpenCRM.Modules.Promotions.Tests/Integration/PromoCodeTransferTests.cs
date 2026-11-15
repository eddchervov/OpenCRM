using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Promotions;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

[Collection(PromotionsDatabaseCollection.Name)]
public sealed class PromoCodeTransferTests(PromotionsDatabaseFixture fixture)
{
    [Fact]
    public async Task HistoricalAccountPromoDoesNotDiscountAnyOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var promo = new PromoCode
        {
            Code = "PG" + Guid.NewGuid().ToString("N").ToUpperInvariant(), Name = "Скидка",
            DiscountTypeCode = PromoCodeCatalogCodes.DiscountTypes.FixedAmount, DiscountValue = 25,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        context.PromoCodeRedemptions.Add(new PromoCodeRedemption
        {
            PromoCode = promo, UserId = organization.OwnerUserId,
            OrganizationId = organization.Id, RedeemedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = new PromoCodeService(context);

        var ownPrice = await service.CalculatePriceAsync(organization.OwnerUserId, organization.Id, 1, 100);
        var foreignPrice = await service.CalculatePriceAsync(organization.OwnerUserId, Guid.NewGuid(), 1, 100);

        Assert.Equal(100, ownPrice.FinalPrice);
        Assert.Equal(0, ownPrice.DiscountAmount);
        Assert.Null(ownPrice.PromoCodeId);
        Assert.Equal(100, foreignPrice.FinalPrice);
        Assert.Null(foreignPrice.PromoCodeId);
        Assert.Equal(1, await context.PromoCodeRedemptions.CountAsync(item => item.PromoCodeId == promo.Id));
    }
}
