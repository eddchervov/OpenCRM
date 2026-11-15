using System.Text;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class ProtectedAdminSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenCRMSessionTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SessionIsEncryptedAndCanBeReadByNewStore()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var address = new Uri("https://opencrm.test/");
        var session = new SavedAdminSession("admin-1", "secret-refresh-token", DateTimeOffset.UtcNow.AddDays(7));
        var store = new ProtectedAdminSessionStore(address, this._directory);
        Assert.Null(store.Load());
        store.Delete();
        store.Save(session);

        var encrypted = File.ReadAllBytes(Assert.Single(Directory.GetFiles(this._directory)));

        Assert.DoesNotContain(session.RefreshToken, Encoding.UTF8.GetString(encrypted));
        Assert.Equal(session, new ProtectedAdminSessionStore(address, this._directory).Load());
        Assert.Null(new ProtectedAdminSessionStore(new Uri("https://another.test/"), this._directory).Load());

        store.Save(session with { RefreshToken = "rotated-refresh-token" });
        Assert.Equal("rotated-refresh-token", store.Load()?.RefreshToken);
        Assert.Single(Directory.GetFiles(this._directory));
        store.Delete();
        Assert.Null(store.Load());
    }

    [Fact]
    public void CorruptedSessionIsRemoved()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new ProtectedAdminSessionStore(new Uri("https://opencrm.test/"), this._directory);
        store.Save(new SavedAdminSession("admin-1", "secret", DateTimeOffset.UtcNow.AddDays(7)));
        File.WriteAllText(Assert.Single(Directory.GetFiles(this._directory)), "Повреждённые данные", Encoding.UTF8);

        Assert.Null(store.Load());
        Assert.Empty(Directory.GetFiles(this._directory));
    }

    [Fact]
    public void CopiedSessionCannotBeDecryptedForAnotherServer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var first = new ProtectedAdminSessionStore(new Uri("https://first.test/"), this._directory);
        var second = new ProtectedAdminSessionStore(new Uri("https://second.test/"), this._directory);
        var session = new SavedAdminSession("admin-1", "secret", DateTimeOffset.UtcNow.AddDays(7));
        first.Save(session);
        var firstFile = Assert.Single(Directory.GetFiles(this._directory));
        second.Save(session);
        var secondFile = Directory.GetFiles(this._directory).Single(file => file != firstFile);
        File.Copy(firstFile, secondFile, true);

        Assert.Null(second.Load());
        Assert.Equal(session, first.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(this._directory))
        {
            Directory.Delete(this._directory, true);
        }
    }
}
