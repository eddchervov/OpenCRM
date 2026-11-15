namespace OpenCRM.Services.Interfaces.Pdf.Dtos;

/// <summary> Отметка повреждения для печати в акте осмотра. </summary>
public sealed class MaintenanceIntakeDamagePdfInfo
{
    public int Number { get; init; }

    public string ViewCode { get; init; } = string.Empty;

    public string ViewName { get; init; } = string.Empty;

    public string DamageTypeName { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public string? Description { get; init; }
}
