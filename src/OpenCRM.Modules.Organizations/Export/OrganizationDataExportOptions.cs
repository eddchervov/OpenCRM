namespace OpenCRM.Modules.Organizations.Export;

public sealed class OrganizationDataExportOptions
{
    public long MaxArchiveBytes { get; set; } = 1024L * 1024 * 1024;

    public long MaxSourceBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    public int MaxTextCharacters { get; set; } = 2_000_000;

    public long MaxJsonFieldBytes { get; set; } = 8L * 1024 * 1024;

    public int MaxFiles { get; set; } = 20_000;

    public long MaxRows { get; set; } = 1_000_000;

    public int TimeoutSeconds { get; set; } = 900;

    public long MinimumFreeDiskBytes { get; set; } = 1024L * 1024 * 1024;

    public string? SpoolDirectory { get; set; }

    public int QueueLimit { get; set; } = 20;

    public int RetryDelaySeconds { get; set; } = 60;

    public int RetentionHours { get; set; } = 24;

    internal string GetSpoolDirectory()
    {
        var directory = this.SpoolDirectory ?? Path.Combine(Path.GetTempPath(), "opencrm-exports");

        return Path.GetFullPath(directory);
    }
}
