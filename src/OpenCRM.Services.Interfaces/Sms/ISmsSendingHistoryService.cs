using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.SmsSendingHistories.Dtos;

namespace OpenCRM.Services.Interfaces.Sms;

/// <summary> Контракт сервиса истории отправки SMS. </summary>
public interface ISmsSendingHistoryService
{
    /// <summary> Возвращает историю отправки SMS. </summary>
    Task<GetSmsSendingHistoryResponse> Get();

    /// <summary> Записывает отправку SMS для входа по телефону. </summary>
    Task<int> CreateLoginByPhone(string phone, string smsCode, string text, string response, string? userIpAddress, Guid? userId);

    /// <summary> Записывает SMS подтверждения телефона при регистрации. </summary>
    Task<int> CreatePhoneConfirmationToRegistration(string phone, string smsCode, string text, string response, string? userIpAddress, Guid? userId);
}

