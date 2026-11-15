using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на генерацию тестовых дефектов. </summary>
public sealed class AdminGenerateTestDefectsRequest
{
    /// <summary> Количество создаваемых дефектов. </summary>
    public int Count { get; init; } = 10;
}

