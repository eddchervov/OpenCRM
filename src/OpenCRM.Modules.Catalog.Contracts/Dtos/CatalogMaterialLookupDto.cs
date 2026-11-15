namespace OpenCRM.Modules.Catalog.Contracts.Dtos;

/// <summary> DTO материала каталога для выбора по идентификатору и названию. </summary>
public sealed record CatalogMaterialLookupDto(Guid Id, string Name);
