namespace OpenCRM.Services.Interfaces.Users.Dtos;

/// <summary> Доступные сведения о клиенте из текущего HTTP-запроса. </summary>
public sealed record UserAccessInfoDto
{
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? Language { get; init; }
    public string? ClientBrands { get; init; }
    public string? Platform { get; init; }
    public bool? IsMobile { get; init; }
    public string? AuthenticationScheme { get; init; }
}
