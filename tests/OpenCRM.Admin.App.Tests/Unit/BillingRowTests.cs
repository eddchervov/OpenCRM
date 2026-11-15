using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

public sealed class BillingRowTests
{
    [Fact]
    public void FutureSubscriptionHasItsOwnStatus()
    {
        var subscription = new AdminBillingOrganizationSubscription
        {
            StartDate = DateTime.UtcNow.AddDays(1),
            EndDate = DateTime.UtcNow.AddDays(31)
        };

        Assert.Equal("Ещё не началась", new BillingRow(subscription).Status);
    }
}
