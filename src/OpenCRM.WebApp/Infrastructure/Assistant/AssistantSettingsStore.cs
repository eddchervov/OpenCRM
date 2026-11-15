using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantSettingsStore(OpenCRMContext context, AssistantOptions options)
{
    public async Task<AssistantSettings> GetAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Set<AssistantSettings>().SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var settings = new AssistantSettings
        {
            IsEnabled = options.Enabled,
            WebEnabled = options.WebEnabled,
            TelegramEnabled = options.TelegramEnabled,
            SpeechRepliesEnabled = options.SpeechRepliesEnabled,
            TextModel = options.TextModel,
            SpeechModel = options.SpeechModel,
            VoiceModel = options.VoiceModel,
            VoiceName = options.VoiceName,
            DailyBudgetRub = options.DailyBudgetRub,
            MonthlyBudgetRub = options.MonthlyBudgetRub,
            TextReservationRub = options.TextReservationRub,
            AudioReservationRub = options.AudioReservationRub,
            MaximumOutputTokens = options.MaximumOutputTokens
        };
        context.Add(settings);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            context.Entry(settings).State = EntityState.Detached;

            return await context.Set<AssistantSettings>().SingleAsync(cancellationToken);
        }

        return settings;
    }
}
