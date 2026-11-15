using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Actions;

internal static class OperationsAssistantLimits
{
    public static int RequirePositive(int value, string settingName)
    {
        return value > 0
            ? value
            : throw new InvalidOperationException($"Проверьте положительное значение Assistant:{settingName}.");
    }

    public static int PageSize(AssistantOptions options)
    {
        return Math.Min(
            RequirePositive(options.LookupPageSize, nameof(options.LookupPageSize)),
            RequirePositive(options.LookupLimit, nameof(options.LookupLimit)));
    }

    public static T[] Candidates<T>(AssistantOptions options, IEnumerable<T> candidates)
    {
        var limit = RequirePositive(options.MaximumCatalogCandidates, nameof(options.MaximumCatalogCandidates));
        var matches = candidates.Take(limit).ToArray();
        if (matches.Length == limit && candidates.Skip(limit).Any())
        {
            throw new InvalidOperationException("Найдено слишком много совпадений. Уточните название или выберите запись на сайте.");
        }

        return matches;
    }

    public static AssistantChoice[] Choices(AssistantOptions options, IEnumerable<AssistantChoice> choices)
    {
        return choices.Take(RequirePositive(options.MaximumClarificationChoices, nameof(options.MaximumClarificationChoices))).ToArray();
    }
}
