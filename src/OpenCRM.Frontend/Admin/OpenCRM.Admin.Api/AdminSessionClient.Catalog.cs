using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    public Task<IReadOnlyList<AdminCatalogBrand>> GetCatalogBrandsAsync(
        int? kind, string search, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminCatalogBrand>>(
            $"api/v1/admin/catalog/brands?kind={kind?.ToString() ?? string.Empty}&search={Uri.EscapeDataString(search)}",
            "Не удалось загрузить марки каталога.", cancellationToken);

    public Task<IReadOnlyList<AdminCatalogModel>> GetCatalogModelsAsync(
        int brandId, string search = "", CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminCatalogModel>>(
            $"api/v1/admin/catalog/brands/{brandId}/models?search={Uri.EscapeDataString(search)}",
            "Не удалось загрузить модели марки.", cancellationToken);

    public Task<AdminOperationResponse> SaveCatalogBrandAsync(
        string name, int kind, int? brandId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kind);
        var method = brandId.HasValue ? HttpMethod.Put : HttpMethod.Post;
        var path = brandId.HasValue ? $"api/v1/admin/catalog/brands/{brandId}" : "api/v1/admin/catalog/brands";

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(method, path,
            new SaveBrandRequest(name.Trim(), kind), "Не удалось сохранить марку.", cancellationToken);
    }

    public Task<AdminOperationResponse> DeleteCatalogBrandAsync(int brandId, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminOperationResponse>(HttpMethod.Delete,
            $"api/v1/admin/catalog/brands/{brandId}", null, "Не удалось удалить марку.", cancellationToken);

    public Task<AdminOperationResponse> SaveCatalogModelAsync(
        int brandId, string name, int? modelId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var method = modelId.HasValue ? HttpMethod.Put : HttpMethod.Post;
        var path = modelId.HasValue
            ? $"api/v1/admin/catalog/models/{modelId}"
            : $"api/v1/admin/catalog/brands/{brandId}/models";

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(method, path,
            new SaveModelRequest(name.Trim()), "Не удалось сохранить модель.", cancellationToken);
    }

    public Task<AdminOperationResponse> DeleteCatalogModelAsync(int modelId, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminOperationResponse>(HttpMethod.Delete,
            $"api/v1/admin/catalog/models/{modelId}", null, "Не удалось удалить модель.", cancellationToken);

    public Task<AdminCatalogSeedResult> SeedVehicleCatalogAsync(CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminCatalogSeedResult>(HttpMethod.Post,
            "api/v1/admin/catalog/seed", null, "Не удалось дополнить каталог техники.", cancellationToken);

    public Task<IReadOnlyList<AdminCatalogSuggestion>> GetCatalogSuggestionsAsync(
        int? status = null, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminCatalogSuggestion>>(
            $"api/v1/admin/catalog-suggestions{(status.HasValue ? $"?status={status}" : string.Empty)}",
            "Не удалось загрузить предложения в каталог.", cancellationToken);

    public Task<AdminOperationResponse> ReviewCatalogSuggestionAsync(
        int suggestionId, bool approve, string? comment, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminOperationResponse>(HttpMethod.Post,
            $"api/v1/admin/catalog-suggestions/{suggestionId}/{(approve ? "approve" : "reject")}",
            new ReviewCatalogSuggestionRequest(comment), "Не удалось обработать предложение.", cancellationToken);

    public Task<AdminOperationResponse> DeleteCatalogSuggestionAsync(int suggestionId, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminOperationResponse>(HttpMethod.Delete,
            $"api/v1/admin/catalog-suggestions/{suggestionId}", null, "Не удалось удалить предложение.", cancellationToken);

    private sealed record SaveBrandRequest(string Name, int Kind);
    private sealed record SaveModelRequest(string Name);
    private sealed record ReviewCatalogSuggestionRequest(string? Comment);
}
