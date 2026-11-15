using Microsoft.AspNetCore.Authorization;
using OpenCRM.Services.Interfaces.Admin;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Sms;
using OpenCRM.Services.Interfaces.SmsSendingHistories.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API просмотра истории отправки SMS для администратора. </summary>
[Route("api/v1/admin/sms-sending-histories")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewCommunications)]
public class SmsSendingHistoriesApiController : ControllerBase
{
    /// <summary> Сервис истории отправки SMS. </summary>
    private readonly ISmsSendingHistoryService _smsSendingHistoryService;

    /// <summary> Создаёт контроллер с сервисом истории SMS. </summary>
    public SmsSendingHistoriesApiController(ISmsSendingHistoryService smsSendingHistoryService)
    {
        _smsSendingHistoryService = smsSendingHistoryService;
    }


    /// <summary> Возвращает страницу истории отправки SMS. </summary>
    [HttpGet]
    public async Task<GetSmsSendingHistoryResponse> Get()
    {
        return await _smsSendingHistoryService.Get();
    }
}



