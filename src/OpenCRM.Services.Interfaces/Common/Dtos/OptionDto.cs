namespace OpenCRM.Services.Interfaces.Common.Dtos;

/// <summary> Универсальная пара идентификатор и название. </summary>
public class OptionDto
{
    /// <summary> Идентификатор варианта. </summary>
    public required int Id { get; set; }

    /// <summary> Название варианта. </summary>
    public required string Name { get; set; }
}

