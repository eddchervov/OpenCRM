namespace OpenCRM.Domain;

/// <summary> Неизменяемые версии рисунка и геометрии схемы приёмки. </summary>
public static class MaintenanceIntakeDiagramVersions
{
    public const string SedanV1 = "sedan-v1";

    public const string Current = SedanV1;

    public static bool IsSupported(string? version) => version == SedanV1;
}
