namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Подтверждённая провайдером идентичность текущего канала организации. </summary>
public sealed record EnsureOrganizationChannelConnectionDto
{
    public Guid OrganizationId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string ImmutableProviderIdentity { get; init; } = string.Empty;
    public string? Username { get; init; }
    public bool IsVerified { get; init; }
    public DateTime VerifiedAt { get; init; }
}
