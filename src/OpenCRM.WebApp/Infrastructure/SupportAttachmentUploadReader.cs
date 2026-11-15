using OpenCRM.Modules.Support.Personal;
using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.WebApp.Infrastructure;

/// <summary> Читает файлы формы с ограничениями до выделения памяти под содержимое. </summary>
internal static class SupportAttachmentUploadReader
{
    public static async Task<IReadOnlyList<SupportAttachmentUploadDto>> ReadAsync(
        IReadOnlyList<IFormFile> files, CancellationToken cancellationToken)
    {
        if (files.Count > SupportAttachmentRules.MaximumCount)
        {
            throw new ArgumentException("Можно прикрепить не более 5 файлов.");
        }

        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
            if (file.Length is <= 0 or > SupportAttachmentRules.MaximumFileSize || total > SupportAttachmentRules.MaximumTotalSize)
            {
                throw new ArgumentException("Выберите непустые файлы: до 10 МБ каждый и до 20 МБ суммарно.");
            }
        }

        var uploads = new List<SupportAttachmentUploadDto>();
        foreach (var file in files)
        {
            var name = SupportAttachmentRules.NormalizeFileName(file.FileName);
            await using var stream = file.OpenReadStream();
            var bytes = new byte[(int)file.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            var extra = new byte[1];
            if (await stream.ReadAsync(extra, cancellationToken) != 0)
            {
                throw new ArgumentException("Размер файла не соответствует запросу.");
            }

            uploads.Add(new SupportAttachmentUploadDto { FileName = name, Content = bytes });
        }

        SupportAttachmentRules.Validate(uploads);

        return uploads;
    }
}
