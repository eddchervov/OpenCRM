using System.Resources;

namespace OpenCRM.Admin.Api;

internal static class AdminApiLocalization
{
    private static readonly ResourceManager s_resources = new("OpenCRM.Admin.Api.Strings", typeof(AdminApiLocalization).Assembly);

    public static string Localize(string language, string message)
    {
        var key = message switch
        {
            "Неверный email или пароль либо сессия истекла." => "error.unauthorized",
            "У учетной записи нет доступа к административной панели." => "error.forbidden",
            "Войдите в административную панель." => "error.loginRequired",
            "Не удалось связаться с сервером. Проверьте подключение и повторите попытку." => "error.network",
            "Превышено время ожидания ответа сервера. Повторите попытку." => "error.timeout",
            "Сервер вернул пустой ответ." => "error.emptyResponse",
            "Сервер вернул некорректный ответ." => "error.invalidResponse",
            "Срок действия сессии истек. Войдите снова." => "error.sessionExpired",
            "Сессия изменилась. Войдите в административную панель снова." => "error.sessionChanged",
            "Превышено время ожидания обновления сессии. Повторите вход." => "error.refreshTimeout",
            "Сервер вернул пустой ответ авторизации." => "error.emptyAuthResponse",
            "Сервер вернул некорректный ответ авторизации." => "error.invalidAuthResponse",
            "Сервер вернул неполные или некорректные данные авторизации." => "error.invalidAuthData",
            "Сервер вернул некорректный профиль администратора." => "error.invalidProfile",
            "Сервер вернул неполный или несогласованный профиль администратора." => "error.inconsistentProfile",
            "У учетной записи нет права Admin.Access." => "error.missingAccess",
            "Не удалось войти в административную панель." => "error.loginFailed",
            "Не удалось загрузить профиль администратора." => "error.profileLoadFailed",
            "Не удалось завершить сессию на сервере." => "error.logoutFailed",
            _ => null
        };
        if (language == "ru")
        {
            return message;
        }

        var culture = AdminUiLanguage.Supported.First(option => option.Code == AdminUiLanguage.Normalize(language)).LanguageTag;
        return AdminApiLocalization.s_resources.GetString(key ?? message, System.Globalization.CultureInfo.GetCultureInfo(culture)) ?? message;
    }
}
