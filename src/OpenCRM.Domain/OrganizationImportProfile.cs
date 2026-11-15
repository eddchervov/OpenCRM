namespace OpenCRM.Domain;

/// <summary> Настройки повторного переноса без исходных строк и персональных данных. </summary>
public sealed class OrganizationImportProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? SourceKey { get; set; }

    public string SettingsJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
