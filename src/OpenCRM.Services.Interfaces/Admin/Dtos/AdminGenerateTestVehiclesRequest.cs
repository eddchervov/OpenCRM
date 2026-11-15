using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на генерацию тестовых транспортных средств. </summary>
public sealed class AdminGenerateTestVehiclesRequest
{
    /// <summary> Количество создаваемых транспортных средств. </summary>
    public int Count { get; init; } = 10;
}

