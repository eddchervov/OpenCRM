namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Снимок доступных функций. </summary>
public class FeatureAccessSnapshotDto
{
    /// <summary> Список функций со значениями доступа. </summary>
    public List<FeatureValueDto> Features { get; set; } = [];
}

