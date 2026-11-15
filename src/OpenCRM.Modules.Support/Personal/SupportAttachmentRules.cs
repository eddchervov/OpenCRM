using System.Text;
using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.Modules.Support.Personal;

/// <summary> Единые ограничения вложений публичной формы и личной переписки. </summary>
public static class SupportAttachmentRules
{
    public const int MaximumCount = 5;
    public const int MaximumFileSize = 10 * 1024 * 1024;
    public const int MaximumTotalSize = 20 * 1024 * 1024;
    public const int MaximumRequestSize = MaximumTotalSize + 1024 * 1024;

    public static string NormalizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl))
        {
            throw new ArgumentException("Укажите имя файла длиной до 200 символов без управляющих символов.");
        }

        return name;
    }

    public static void Validate(IReadOnlyList<SupportAttachmentUploadDto> uploads)
    {
        if (uploads.Count > MaximumCount)
        {
            throw new ArgumentException("К одному сообщению можно прикрепить не более 5 файлов.");
        }

        long total = 0;
        foreach (var upload in uploads)
        {
            var name = SupportAttachmentRules.NormalizeFileName(upload.FileName);
            var bytes = upload.Content.AsSpan();
            total += bytes.Length;
            if (bytes.Length is <= 0 or > MaximumFileSize || total > MaximumTotalSize)
            {
                throw new ArgumentException("Выберите непустые файлы: до 10 МБ каждый и до 20 МБ суммарно.");
            }

            var isValid = Path.GetExtension(name).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
                ".png" => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                ".webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
                ".pdf" => bytes.StartsWith("%PDF-"u8),
                ".txt" or ".log" => SupportAttachmentRules.IsText(bytes),
                _ => false
            };

            if (!isValid)
            {
                throw new ArgumentException("Допустимы изображения JPEG, PNG, WebP, PDF и текстовые файлы TXT, LOG. Содержимое должно соответствовать формату.");
            }
        }
    }

    private static bool IsText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Contains((byte)0))
        {
            return false;
        }

        try
        {
            new UTF8Encoding(false, true).GetCharCount(bytes);

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
