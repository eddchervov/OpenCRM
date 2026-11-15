using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.SmsSendingHistories;
using OpenCRM.Services.Interfaces.Sms;
using OpenCRM.Services.Interfaces.SmsSendingHistories.Dtos;
using System.Linq.Expressions;

namespace OpenCRM.Modules.Notifications.Sms;

/// <summary> Сервис истории отправки SMS. </summary>
public class SmsSendingHistoryService(
        ISmsSendingHistoryRepository smsSendingHistoryRepository,
        ISystemClock systemClock
    ) : ISmsSendingHistoryService
{
    /// <summary> Зависимость репозитория истории отправки SMS. </summary>
    private readonly ISmsSendingHistoryRepository _smsSendingHistoryRepository = smsSendingHistoryRepository;

    /// <summary> Зависимость системных часов. </summary>
    private readonly ISystemClock _systemClock = systemClock;

    /// <summary> Возвращает историю отправки SMS. </summary>
    public async Task<GetSmsSendingHistoryResponse> Get()
    {
        int take = 100;
        var histories = await _smsSendingHistoryRepository.Get(MapToDtoExpr).OrderByDescending(x => x.DateTime).Take(take).ToListAsync();
        var totalCount = await _smsSendingHistoryRepository.Count();

        return new GetSmsSendingHistoryResponse
        {
            TotalCount = totalCount,
            Histories = histories,
        };
    }

    /// <summary> Выражение проекции истории SMS в DTO. </summary>
    private static readonly Expression<Func<SmsSendingHistory, GetSmsSendingHistoryDto>> MapToDtoExpr = history => new GetSmsSendingHistoryDto
    {
        Id = history.Id,
        UserIpAddress = history.UserIpAddress,
        UserId = history.UserId,
        Text = history.Text,
        DateTime = history.DateTime,
        Response = history.Response,
        SMSCode = history.SMSCode,
        Phone = history.Phone,
        Type = history.Type
    };

    /// <summary> Записывает историю входа по SMS-коду. </summary>
    public async Task<int> CreateLoginByPhone(string phone, string smsCode, string text, string response, string? userIpAddress, Guid? userId)
    {
        var smsSendingHistory = Create(phone, smsCode, text, response, userIpAddress, userId, SmsSendingHistoryType.LoginByPhone);

        await _smsSendingHistoryRepository.Add(smsSendingHistory);
        await _smsSendingHistoryRepository.Save();

        return smsSendingHistory.Id;
    }

    /// <summary> Записывает историю подтверждения телефона при регистрации. </summary>
    public async Task<int> CreatePhoneConfirmationToRegistration(string phone, string smsCode, string text, string response, string? userIpAddress, Guid? userId)
    {
        var smsSendingHistory = Create(phone, smsCode, text, response, userIpAddress, userId, SmsSendingHistoryType.PhoneConfirmationToRegistration);

        await _smsSendingHistoryRepository.Add(smsSendingHistory);
        await _smsSendingHistoryRepository.Save();

        return smsSendingHistory.Id;
    }

    /// <summary> Создаёт запись истории отправки SMS указанного типа. </summary>
    private SmsSendingHistory Create(string phone, string smsCode, string text, string response, string? userIpAddress, Guid? userId, SmsSendingHistoryType type)
    {
        return new SmsSendingHistory
        {
            DateTime = _systemClock.UtcNow,
            Phone = phone,
            SMSCode = smsCode,
            Text = text,
            Type = type,
            UserId = userId,
            Response = response,
            UserIpAddress = userIpAddress,
        };
    }
}
