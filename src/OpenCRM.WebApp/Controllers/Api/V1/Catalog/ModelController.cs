using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Models;
using OpenCRM.Services.Interfaces.Models.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Catalog
{
    /// <summary> API выбора моделей техники в каталоге. </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/catalog/models")]
    public class ModelController(IModelService modelService) : Controller
    {
        /// <summary> Сервис работы с моделями. </summary>
        private readonly IModelService _modelService = modelService;

        /// <summary> Возвращает список моделей марки для выбора. </summary>
        [HttpGet("options")]
        public async Task<IEnumerable<ModelOptionDto>> Get(int brandId, VehicleKind? kind = null)
        {
            return await _modelService.GetOptions(brandId, kind);
        }
    }
}

