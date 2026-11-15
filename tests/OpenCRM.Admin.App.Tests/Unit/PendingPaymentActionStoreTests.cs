using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

public sealed class PendingPaymentActionStoreTests
{
    [Fact]
    public async Task PendingOperationsAreIsolatedByHostAndAdministrator()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new PendingPaymentActionStore(directory);
        var draft = new PendingPaymentActionDraft(42,
            new AdminBillingPaymentServiceActionRequest(Guid.NewGuid(), "refund", "Проверка", 125m, "state-token"));
        try
        {
            await store.SaveAsync("https://qa.example:7246:admin-a", draft, TestContext.Current.CancellationToken);

            Assert.Equal(draft, await store.LoadAsync("https://qa.example:7246:admin-a", TestContext.Current.CancellationToken));
            Assert.Null(await store.LoadAsync("https://qa.example:7246:admin-b", TestContext.Current.CancellationToken));
            Assert.Null(await store.LoadAsync("https://production.example:443:admin-a", TestContext.Current.CancellationToken));

            await store.DeleteAsync("https://qa.example:7246:admin-b");

            Assert.Equal(draft, await store.LoadAsync("https://qa.example:7246:admin-a", TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
