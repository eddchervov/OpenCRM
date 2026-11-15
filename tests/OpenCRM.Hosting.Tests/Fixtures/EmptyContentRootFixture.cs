namespace OpenCRM.Hosting.Tests.Fixtures;

public sealed class EmptyContentRootFixture : IDisposable
{
    public EmptyContentRootFixture()
    {
        this.ContentRoot = Path.Combine(Path.GetTempPath(), $"OpenCRM.Hosting.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.ContentRoot);
    }

    public string ContentRoot { get; }

    public void Dispose()
    {
        if (Directory.Exists(this.ContentRoot))
        {
            Directory.Delete(this.ContentRoot, recursive: true);
        }
    }
}