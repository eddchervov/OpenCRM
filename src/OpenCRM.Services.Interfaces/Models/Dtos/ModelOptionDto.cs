using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Models.Dtos;

/// <summary> Вариант модели для выбора. </summary>
public record ModelOptionDto(int Id, string Name, VehicleKind Kind);

