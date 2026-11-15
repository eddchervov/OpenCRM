using OpenCRM.Domain.Pagination;
using OpenCRM.Modules.Promotions.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Promotions.Contracts;

/// <summary> Контракт работы с промокодами организаций. </summary>
public interface IOrganizationPromoCodeService
{
    /// <summary> Применяет промокод к организации. </summary>
    Task<OrganizationPromoApplicationDto> ApplyAsync(string actorUserId, Guid organizationId, string code);

    /// <summary> Возвращает применения промокодов организации. </summary>
    Task<IReadOnlyList<OrganizationPromoApplicationDto>> GetApplicationsAsync(string actorUserId, Guid organizationId);

    /// <summary> Возвращает страницу применений промокодов организации. </summary>
    Task<PagedResult<OrganizationPromoApplicationDto>> GetApplicationsPageAsync(string actorUserId, Guid organizationId, int page, int pageSize);

    /// <summary> Рассчитывает скидку для организации по тарифному плану. </summary>
    Task<PromotionPriceDto> CalculateDiscountAsync(Guid organizationId, int planId, decimal originalPrice);
}
