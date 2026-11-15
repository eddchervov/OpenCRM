using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Brands;
using OpenCRM.Services.Interfaces.Brands.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Catalog
{
    /// <summary> API выбора марок техники в каталоге. </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/catalog/brands")]
    public class BrandController : Controller
    {
        /// <summary> Сервис работы с марками. </summary>
        private readonly IBrandService _brandService;

        /// <summary> Кэш в памяти для справочных данных. </summary>
        private readonly IMemoryCache _memoryCache;

        /// <summary> Средство журналирования контроллера. </summary>
        private readonly ILogger<BrandController> _logger;

        /// <summary> Создаёт контроллер с сервисом марок и кэшем. </summary>
        public BrandController(
            IBrandService brandService,
            IMemoryCache memoryCache,
            ILogger<BrandController> logger)
        {
            _brandService = brandService;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        /// <summary> Возвращает список марок для выбора. </summary>
        [HttpGet("options")]
        public async Task<IEnumerable<BrandOptionDto>> Get(VehicleKind? kind = null)
        {
            var cacheKey = $"brand_options_{(int?)kind ?? 0}";

            if (!_memoryCache.TryGetValue(cacheKey, out IEnumerable<BrandOptionDto>? cachedOptions))
            {
                _logger.LogInformation("Brand options cache miss, fetching from service");

                var options = await _brandService.GetOptions(kind);

                cachedOptions = _memoryCache.Set(cacheKey, options, TimeSpan.FromMinutes(20));
            }

            return cachedOptions ?? new List<BrandOptionDto>();
        }
    }
}

