namespace OpenCRM.Admin.Api.Models;

/// <summary> Данные для восстановления сессии без сохранения пароля и токена доступа. </summary>
public sealed record SavedAdminSession(string UserId, string RefreshToken, DateTimeOffset ExpiresAt);
