using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Documents.Templates;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Общие компоненты оформления PDF-документов. </summary>
internal sealed class PdfDocumentComponents
{
    /// <summary> Название основного шрифта документов. </summary>
    internal const string FontFamily = "Arial";

    /// <summary> Основной цвет оформления. </summary>
    internal const string PrimaryColor = "#1F4E79";

    /// <summary> Светлый вариант основного цвета. </summary>
    internal const string PrimaryLightColor = "#EAF2F8";

    /// <summary> Акцентный цвет оформления. </summary>
    internal const string AccentColor = "#2E7D32";

    /// <summary> Цвет текста документов. </summary>
    internal const string TextColor = "#101828";

    /// <summary> Приглушённый цвет текста. </summary>
    internal const string MutedColor = "#667085";

    /// <summary> Цвет границ элементов. </summary>
    internal const string BorderColor = "#D0D5DD";

    /// <summary> Мягкий цвет заливки для PDF-документов. </summary>
    internal const string SoftFillColor = "#F8FAFC";

    /// <summary> Акцентный цвет предупреждений в PDF-документах. </summary>
    internal const string DangerColor = "#B42318";

    /// <summary> Возвращает подпись показателя пробега для единицы измерения. </summary>
    internal static string GetMileageLabel(VehicleMileageUnit mileageUnit)
    {
        return MaintenanceDocumentFacts.GetMileageLabel(mileageUnit) + ":";
    }

    /// <summary> Отрисовывает показатель с подписью и значением. </summary>
    internal static void RenderMetric(IContainer container, string label, string value, string color = PrimaryColor)
    {
        container.Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(value)
                .FontSize(18)
                .Bold()
                .FontColor(color);
            column.Item().Text(label)
                .FontSize(9)
                .FontColor(MutedColor);
        });
    }

    /// <summary> Отрисовывает шапку документа по шаблону сценария. </summary>
    internal static void RenderScenarioDocumentHeader(IContainer container, MaintenanceDocumentTemplate preset, string title, string dateText, string projectName)
    {
        container.BorderBottom(2).BorderColor(preset.AccentColorHex).PaddingBottom(10).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text(preset.Label).FontSize(8.5f).Bold().FontColor(preset.AccentColorHex);
            column.Item().Text($"{title} от {dateText}").FontSize(16).Bold().FontColor(PrimaryColor).AlignCenter();
            column.Item().Text(preset.Subtitle).FontSize(9).FontColor(MutedColor).AlignCenter();
        });
    }

    /// <summary> Отрисовывает блок информации о технике. </summary>
    internal static void RenderVehicleInfo(IContainer container, VehicleInfo? vehicle)
    {
        if (vehicle == null)
        {
            RenderEmptyState(container, "Данные техники отсутствуют");
            return;
        }

        container.Element(Card).Column(column =>
        {
            column.Spacing(10);
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => RenderFact(c, "Марка", vehicle.BrandName));
                row.RelativeItem().Element(c => RenderFact(c, "Модель", vehicle.ModelName));
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => RenderFact(c, "Год выпуска", vehicle.YearIssue.HasValue ? vehicle.YearIssue.Value.ToString() : "Не указан"));
                row.RelativeItem().Element(c => RenderFact(c, "Название", vehicle.Name ?? "Не указано"));
            });

            if (!string.IsNullOrWhiteSpace(vehicle.Description))
            {
                column.Item().Element(c => RenderFact(c, "Описание", vehicle.Description));
            }
        });
    }

    /// <summary> Отрисовывает подвал PDF-документа. </summary>
    internal static void RenderFooter(IContainer container, string projectName)
    {
        container.PaddingTop(8).BorderTop(1).BorderColor(PrimaryLightColor).Row(row =>
        {
            row.RelativeItem().Text(projectName)
                .FontSize(8)
                .FontColor(MutedColor);

            row.AutoItem().Text(text =>
            {
                text.DefaultTextStyle(TextStyle.Default.FontFamily(FontFamily).FontSize(8).FontColor(MutedColor));
                text.Span("Страница ");
                text.CurrentPageNumber();
                text.Span(" из ");
                text.TotalPages();
            });
        });
    }

    /// <summary> Отрисовывает шапку документа с заголовком, подзаголовком и датой. </summary>
    internal static void RenderDocumentHeader(IContainer container, string title, string subtitle, string dateText, string projectName)
    {
        container.Column(column =>
        {
            column.Spacing(6);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(titleColumn =>
                {
                    titleColumn.Spacing(3);
                    titleColumn.Item().Text(title)
                        .FontSize(20)
                        .Bold()
                        .FontColor(PrimaryColor);

                    if (!string.IsNullOrWhiteSpace(subtitle))
                    {
                        titleColumn.Item().Text(subtitle)
                            .FontSize(11)
                            .FontColor(MutedColor);
                    }
                });

                row.ConstantItem(115).AlignRight().Column(metaColumn =>
                {
                    metaColumn.Spacing(2);
                    metaColumn.Item().AlignRight().Text(projectName)
                        .FontSize(9)
                        .SemiBold()
                        .FontColor(PrimaryColor);
                    metaColumn.Item().AlignRight().Text(dateText ?? string.Empty)
                        .FontSize(9)
                        .FontColor(MutedColor);
                });
            });

            column.Item().PaddingTop(8).BorderBottom(1).BorderColor(PrimaryLightColor);
        });
    }

    /// <summary> Возвращает текстовое описание единицы измерения пробега. </summary>
    internal static string GetMileageUnitText(VehicleMileageUnit mileageUnit)
    {
        return MaintenanceDocumentFacts.GetMileageUnit(mileageUnit);
    }

    /// <summary> Отрисовывает факт с подписью и значением. </summary>
    internal static void RenderFact(IContainer container, string label, string? value)
    {
        container.Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(label)
                .FontSize(8.5f)
                .FontColor(MutedColor);
            column.Item().Text(string.IsNullOrWhiteSpace(value) ? "-" : value)
                .FontSize(10)
                .SemiBold()
                .FontColor(TextColor);
        });
    }

    /// <summary> Отрисовывает раздел с заголовком и содержимым. </summary>
    internal static void RenderSection(IContainer container, string title, Action<IContainer> content)
    {
        container.EnsureSpace(80).Column(column =>
        {
            column.Spacing(8);
            column.Item().Text(title)
                .FontSize(13)
                .Bold()
                .FontColor(PrimaryColor);
            column.Item().Element(content);
        });
    }

    /// <summary> Отрисовывает запись об обслуживании, при необходимости с фотографиями. </summary>
    internal static void RenderMaintenanceRecord(IContainer container, MaintenanceInfo maintenance, VehicleMileageUnit mileageUnit, bool includePhotos)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            column.Item().Element(c => RenderSection(c, "Обслуживание", section => RenderMaintenanceInfo(section, maintenance, mileageUnit)));

            if (maintenance.MaintenanceDetails?.Count > 0)
            {
                column.Item().Element(c => RenderSection(c, "Работы и материалы", section => PdfTableComponents.RenderServices(section, maintenance.MaintenanceDetails, currencyCode: maintenance.CurrencyCode)));
            }

            if (!string.IsNullOrWhiteSpace(maintenance.Comment))
            {
                column.Item().Element(c => RenderSection(c, "Комментарий", section => PdfTableComponents.RenderComment(section, maintenance.Comment)));
            }

            if (includePhotos && maintenance.Photos?.Any() == true)
            {
                column.Item().Element(c => RenderSection(c, $"Фотографии ({maintenance.PhotosCount})", section => PdfPhotoComponents.RenderPhotos(section, maintenance.Photos)));
            }
        });
    }

    /// <summary> Отрисовывает пустое состояние с сообщением. </summary>
    internal static void RenderEmptyState(IContainer container, string message)
    {
        container
            .Border(1)
            .BorderColor(BorderColor)
            .Background(SoftFillColor)
            .Padding(16)
            .AlignCenter()
            .Text(message)
            .FontSize(10)
            .Italic()
            .FontColor(MutedColor);
    }

    /// <summary> Настраивает параметры страницы PDF-документа. </summary>
    internal static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(1.7f, Unit.Centimetre);
        page.MarginVertical(1.4f, Unit.Centimetre);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(TextStyle.Default
            .FontFamily(FontFamily)
            .FontSize(10.5f)
            .FontColor(TextColor)
            .LineHeight(1.2f));
    }

    /// <summary> Отрисовывает фотографию техники по изображению в байтах. </summary>
    internal static void RenderVehiclePhoto(IContainer container, byte[] imageData)
    {
        container.Element(Card).Column(column =>
        {
            column.Spacing(10);
            column.Item().Text("Фотография техники")
                .FontSize(12)
                .Bold()
                .FontColor(PrimaryColor);

            PdfPhotoComponents.RenderImage(column.Item(), imageData, 500, 330);
        });
    }

    /// <summary> Возвращает заголовок техники или плейсхолдер, если данных нет. </summary>
    internal static string GetVehicleTitle(VehicleInfo? vehicle)
    {
        if (vehicle == null) return string.Empty;

        return MaintenanceDocumentFacts.GetVehicleTitle(vehicle.BrandName, vehicle.ModelName, vehicle.Name, vehicle.RegistrationPlate);
    }

    /// <summary> Возвращает текстовое описание типа обслуживания по коду. </summary>
    internal static string GetMaintenanceTypeText(int type)
    {
        return MaintenanceDocumentFacts.GetMaintenanceTypeText(type);
    }

    /// <summary> Отрисовывает блок статистики с количеством и суммой. </summary>
    internal static void RenderStatistics(IContainer container, int totalCount, IReadOnlyDictionary<string, decimal> currencyTotals)
    {
        container.Element(Card).Column(column =>
        {
            column.Item().Element(c => RenderMetric(c, "Записей обслуживания", totalCount.ToString()));
            foreach (var currencyTotal in currencyTotals.OrderBy(total => total.Key))
            {
                column.Item().Element(c => RenderMetric(c, $"Затраты, {currencyTotal.Key}", FormatMoney(currencyTotal.Value, currencyTotal.Key), AccentColor));
            }
        });
    }

    /// <summary> Отрисовывает заполняемый факт со значением в одну строку. </summary>
    internal static void RenderInlineWritableFact(IContainer container, string label, string? value)
    {
        container.Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(label)
                .FontSize(8.5f)
                .FontColor(MutedColor);

            if (string.IsNullOrWhiteSpace(value))
            {
                column.Item().BorderBottom(1).BorderColor(TextColor).Height(14);
            }
            else
            {
                column.Item().Text(value)
                    .FontSize(10)
                    .SemiBold()
                    .FontColor(TextColor);
            }
        });
    }

    /// <summary> Отрисовывает блок подписи с должностью и именем. </summary>
    internal static void RenderSignatureBlock(IContainer container, string title, string position, string name)
    {
        container.Column(column =>
        {
            column.Spacing(18);
            column.Item().Text(title)
                .FontSize(10)
                .Bold()
                .FontColor(PrimaryColor);
            column.Item().BorderBottom(1).BorderColor(TextColor).Height(22);
            var caption = string.Join(" ", new[] { position, name }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!string.IsNullOrWhiteSpace(caption))
            {
                column.Item().Text(caption)
                    .FontSize(8.5f)
                    .FontColor(MutedColor)
                    .AlignCenter();
            }
            else
            {
                column.Item().Text("подпись, расшифровка")
                    .FontSize(8.5f)
                    .FontColor(MutedColor)
                    .AlignCenter();
            }
        });
    }

    /// <summary> Форматирует денежное значение для отображения. </summary>
    internal static string FormatMoney(decimal value, string currencyCode = "RUB")
    {
        return $"{value:n2} {currencyCode}";
    }

    /// <summary> Оформляет контейнер как карточку с рамкой и фоном. </summary>
    internal static IContainer Card(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(BorderColor)
            .Background(SoftFillColor)
            .Padding(12);
    }

    /// <summary> Отрисовывает панель с датой формирования и признаком фотографий. </summary>
    internal static void RenderGenerationPanel(IContainer container, string generatedDate, bool includePhotos, string projectName)
    {
        container.Element(Card).Column(column =>
        {
            column.Spacing(8);
            column.Item().Text("Параметры отчета")
                .FontSize(12)
                .Bold()
                .FontColor(PrimaryColor);

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => RenderFact(c, "Дата формирования", generatedDate));
                row.RelativeItem().Element(c => RenderFact(c, "Фотографии", includePhotos ? "Включены" : "Не включены"));
                row.RelativeItem().Element(c => RenderFact(c, "Система", projectName));
            });
        });
    }

    /// <summary> Отрисовывает блок информации об обслуживании, если оно указано. </summary>
    internal static void RenderMaintenanceInfo(IContainer container, MaintenanceInfo? maintenance, VehicleMileageUnit mileageUnit)
    {
        if (maintenance == null)
        {
            RenderEmptyState(container, "Данные обслуживания отсутствуют");
            return;
        }

        container.Element(Card).Column(column =>
        {
            column.Spacing(10);
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => RenderFact(c, "Тип", GetMaintenanceTypeText((int)maintenance.Type)));
                row.RelativeItem().Element(c => RenderFact(c, "Дата", maintenance.Date));
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => RenderFact(c, GetMileageLabel(mileageUnit).TrimEnd(':'), maintenance.Mileage.HasValue ? $"{maintenance.Mileage.Value:n0} {GetMileageUnitText(mileageUnit)}" : "-"));
                row.RelativeItem().Element(c => RenderFact(c, "Стоимость", maintenance.Price.HasValue && maintenance.Price > 0 ? FormatMoney(maintenance.Price.Value, maintenance.CurrencyCode) : "-"));
            });

            if (!string.IsNullOrWhiteSpace(maintenance.Location) || !string.IsNullOrWhiteSpace(maintenance.Performer))
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => RenderFact(c, "Место", maintenance.Location));
                    row.RelativeItem().Element(c => RenderFact(c, "Исполнитель", maintenance.Performer));
                });
            }
        });
    }
}
