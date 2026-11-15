namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Безопасный для показа владельцу отказ из-за ограничения ресурсов выгрузки. </summary>
public sealed class OrganizationDataExportResourceLimitException(string message) : InvalidOperationException(message);
