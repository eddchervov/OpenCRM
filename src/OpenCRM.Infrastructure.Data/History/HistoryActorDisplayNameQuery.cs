using Microsoft.EntityFrameworkCore;

namespace OpenCRM.Infrastructure.Data.History;

/// <summary> Получает отображаемое имя автора для записи в историю. </summary>
public static class HistoryActorDisplayNameQuery
{
    /// <summary> Возвращает имя пользователя с учётом сохранённого адреса удалённого аккаунта. </summary>
    public static async Task<string> ResolveAsync(OpenCRMContext context, string userId)
    {
        var actor = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Email,
                user.UserName,
                user.OriginalEmail,
                user.IsDeleted
            })
            .FirstOrDefaultAsync();

        if (actor == null)
        {
            return "Неизвестный пользователь";
        }

        var displayName = actor.IsDeleted
            ? actor.OriginalEmail
            : actor.Email;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = actor.UserName;
        }

        return string.IsNullOrWhiteSpace(displayName)
            ? "Пользователь"
            : displayName;
    }
}
