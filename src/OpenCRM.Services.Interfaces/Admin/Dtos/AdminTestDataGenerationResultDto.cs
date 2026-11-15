using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат генерации тестовых данных. </summary>
public sealed class AdminTestDataGenerationResultDto
{
    /// <summary> Признак успешной генерации. </summary>
    public bool Succeeded { get; init; }

    /// <summary> Признак того, что сущность не найдена. </summary>
    public bool NotFound { get; init; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; init; }

    /// <summary> Сообщение о результате генерации. </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary> Количество созданных сущностей. </summary>
    public int CreatedCount { get; init; }
}

