using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO сохранения модели для админ-панели. </summary>
public sealed class AdminSaveModelDto
{
    /// <summary> Название модели. </summary>
    public string? Name { get; init; }
}

