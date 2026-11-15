using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на генерацию тестовых обслуживаний. </summary>
public sealed class AdminGenerateTestMaintenancesRequest
{
    /// <summary> Количество создаваемых обслуживаний. </summary>
    public int Count { get; init; } = 20;

    /// <summary> Режим генерации. </summary>
    public string? Mode { get; init; }
}

