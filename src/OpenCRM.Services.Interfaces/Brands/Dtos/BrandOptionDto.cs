using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Brands.Dtos;

/// <summary> Вариант бренда для выбора. </summary>
public record BrandOptionDto(int Id, string Name, VehicleKind Kind);

