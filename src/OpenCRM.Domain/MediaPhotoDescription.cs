namespace OpenCRM.Domain;

/// <summary>Описание набора фотографий для истории изменений.</summary>
public static class MediaPhotoDescription
{
    public static string Build(string label, IEnumerable<string?> fileNames)
    {
        var names = fileNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToList();

        return names.Count == 0
            ? $"{label}: не указано"
            : $"{label}: {string.Join(", ", names)}";
    }
}
