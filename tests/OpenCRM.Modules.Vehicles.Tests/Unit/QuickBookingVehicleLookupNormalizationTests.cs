using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using Moq;

namespace OpenCRM.Modules.Vehicles.Tests.Unit;

public sealed class QuickBookingVehicleLookupNormalizationTests
{
    [Theory]
    [InlineData("A123AA154", "А123АА154")]
    [InlineData("а123аа154", "А123АА154")]
    [InlineData("A-123 AA 154", "А123АА154")]
    [InlineData(" - . ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizesPlateWithLatinCyrillicMapAndRemovesPunctuation(string? value, string expected)
    {
        using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>().Options);
        var lookup = new QuickBookingVehicleLookupService(context, Mock.Of<IOrganizationSubscriptionService>());

        var normalized = lookup.NormalizePlateForSearch(value);

        Assert.Equal(expected, normalized);
    }
}
