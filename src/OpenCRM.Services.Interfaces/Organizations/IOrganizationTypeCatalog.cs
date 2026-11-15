using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Каталог типов организаций для создания рабочих пространств. </summary>
public interface IOrganizationTypeCatalog
{
    /// <summary> Возвращает варианты создания, сохраняя настроенные названия и порядок. </summary>
    Task<IReadOnlyList<OrganizationTypeOptionDto>> GetOrganizationTypeOptionsAsync();
}
