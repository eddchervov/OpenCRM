using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents.Word.Rendering;

/// <summary> Общие компоненты оформления Word-документов. </summary>
internal sealed class WordDocumentComponents
{
    /// <summary> Основное семейство шрифтов Word-документов. </summary>
    internal const string FontFamily = "Arial";

    /// <summary> Основной цвет текста Word-документов. </summary>
    internal const string PrimaryColor = "1F4E79";

    /// <summary> Акцентный цвет Word-документов. </summary>
    internal const string AccentColor = "2E7D32";

    /// <summary> Приглушённый цвет текста Word-документов. </summary>
    internal const string MutedColor = "667085";

    /// <summary> Цвет рамок Word-документов. </summary>
    internal const string BorderColor = "D0D5DD";

    /// <summary> Цвет заливки разделов Word-документов. </summary>
    internal const string SectionFill = "F8FAFC";

    /// <summary> Цвет заливки шапки Word-документов. </summary>
    internal const string HeaderFill = "EAF2F8";

    /// <summary> Цвет заливки заголовков таблиц Word-документов. </summary>
    internal const string TableHeaderFill = "1F4E79";

    /// <summary> Возвращает подпись показателя пробега для единицы измерения. </summary>
    internal static string GetMileageLabel(VehicleMileageUnit mileageUnit)
    {
        return MaintenanceDocumentFacts.GetMileageLabel(mileageUnit) + ":";
    }

    /// <summary> Добавляет заголовок раздела в документ. </summary>
    internal static void AddSectionHeading(Body body, string text)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new ParagraphBorders(
                    new BottomBorder { Val = BorderValues.Single, Size = 8, Color = HeaderFill }
                ),
                new SpacingBetweenLines { Before = "80", After = "120" }
            ),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontFamily, HighAnsi = FontFamily, ComplexScript = FontFamily },
                    new Bold(),
                    new Color { Val = PrimaryColor },
                    new FontSize { Val = "24" }
                ),
                new Text(text)
            )
        ));
    }

    /// <summary> Добавляет вводный блок документа сценария. </summary>
    internal static void AddScenarioIntro(Body body, string label, string intro, string accentColor)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            WordTableComponents.CreateTableBorders(accentColor, BorderValues.Single, 8),
            new Shading { Val = ShadingPatternValues.Clear, Fill = SectionFill },
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "140", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "140", Type = TableWidthUnitValues.Dxa }
            )
        ));

        table.AppendChild(new TableGrid(new GridColumn { Width = "5000" }));

        var row = table.AppendChild(new TableRow());
        var cell = row.AppendChild(new TableCell());
        cell.AppendChild(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));
        cell.AppendChild(WordTableComponents.CreateCellParagraph(label, JustificationValues.Left, bold: true, color: accentColor));
        cell.AppendChild(WordTableComponents.CreateCellParagraph(intro, JustificationValues.Left, color: "101828"));

        body.AppendChild(table);
        AddSpacer(body, 140);
    }

    /// <summary> Форматирует строку с подписью и необязательным значением. </summary>
    internal static string? FormatOptional(string label, string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : $"{label} {value}";
    }

    /// <summary> Возвращает форму слова для указанного количества. </summary>
    internal static string GetCorrectWordForm(int count)
    {
        var lastDigit = count % 10;
        var lastTwoDigits = count % 100;

        if (lastTwoDigits is >= 11 and <= 19)
            return $"{count} услуг";

        return lastDigit switch
        {
            1 => $"{count} услуга",
            >= 2 and <= 4 => $"{count} услуги",
            _ => $"{count} услуг"
        };
    }

    /// <summary> Добавляет блок информации о технике в документ. </summary>
    internal static void AddVehicleInfo(Body body, WordVehicleInfo? vehicle)
    {
        if (vehicle == null)
        {
            AddEmptyState(body, "Данные техники отсутствуют");
            return;
        }

        var table = WordTableComponents.CreateInfoTable();
        WordTableComponents.AddInfoRow(table, "Марка", vehicle.BrandName);
        WordTableComponents.AddInfoRow(table, "Модель", vehicle.ModelName);
        WordTableComponents.AddInfoRow(table, "Год выпуска", vehicle.YearIssue.HasValue ? vehicle.YearIssue.Value.ToString() : "Не указан");
        WordTableComponents.AddInfoRow(table, "Название", vehicle.Name ?? "Не указано");

        if (!string.IsNullOrWhiteSpace(vehicle.Description))
        {
            WordTableComponents.AddInfoRow(table, "Описание", vehicle.Description);
        }

        body.AppendChild(table);
        AddSpacer(body, 120);
    }

    /// <summary> Добавляет запись об обслуживании в документ. </summary>
    internal static void AddMaintenanceRecord(Body body, WordMaintenanceInfo maintenance, int index, VehicleMileageUnit mileageUnit)
    {
        if (maintenance == null) return;

        var title = $"{index}. {GetMaintenanceTypeText(maintenance.Type)}";
        if (!string.IsNullOrWhiteSpace(maintenance.Date))
        {
            title += $" от {maintenance.Date}";
        }

        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new Shading { Val = ShadingPatternValues.Clear, Fill = HeaderFill },
                new SpacingBetweenLines { Before = "160", After = "80" }
            ),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontFamily, HighAnsi = FontFamily, ComplexScript = FontFamily },
                    new Bold(),
                    new Color { Val = PrimaryColor },
                    new FontSize { Val = "22" }
                ),
                new Text(title)
            )
        ));

        var table = WordTableComponents.CreateInfoTable();

        if (maintenance.Mileage.HasValue)
        {
            WordTableComponents.AddInfoRow(table, GetMileageLabel(mileageUnit).TrimEnd(':'), $"{maintenance.Mileage.Value:N0} {GetMileageUnitText(mileageUnit)}");
        }

        if (!string.IsNullOrWhiteSpace(maintenance.Location))
        {
            WordTableComponents.AddInfoRow(table, "Место", maintenance.Location);
        }

        if (!string.IsNullOrWhiteSpace(maintenance.Performer))
        {
            WordTableComponents.AddInfoRow(table, "Исполнитель", maintenance.Performer);
        }

        if (maintenance.Price.HasValue && maintenance.Price > 0)
        {
            WordTableComponents.AddInfoRow(table, "Стоимость", FormatMoney(maintenance.Price.Value));
        }

        body.AppendChild(table);

        if (maintenance.MaintenanceDetails?.Any() == true)
        {
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "80", After = "40" }),
                new Run(
                    new RunProperties(new Bold(), new Color { Val = MutedColor }),
                    new Text($"Работы и материалы: {GetCorrectWordForm(maintenance.MaintenanceDetails.Count)}")
                )
            ));

            WordTableComponents.AddServicesTable(body, maintenance.MaintenanceDetails);
        }

        if (!string.IsNullOrWhiteSpace(maintenance.Comment))
        {
            var tableComment = WordTableComponents.CreateInfoTable();
            WordTableComponents.AddInfoRow(tableComment, "Комментарий", maintenance.Comment);
            body.AppendChild(tableComment);
        }
    }

    /// <summary> Добавляет комментарий в документ. </summary>
    internal static void AddComment(Body body, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return;

        AddSectionHeading(body, "Комментарий");
        var table = WordTableComponents.CreateInfoTable();
        var row = table.AppendChild(new TableRow());
        var cell = row.AppendChild(new TableCell());
        cell.AppendChild(new TableCellProperties(
            new GridSpan { Val = 2 },
            new Shading { Val = ShadingPatternValues.Clear, Fill = SectionFill }
        ));
        cell.AppendChild(WordTableComponents.CreateCellParagraph(comment, JustificationValues.Left));
        body.AppendChild(table);
        AddSpacer(body, 120);
    }

    /// <summary> Возвращает текстовое описание единицы измерения пробега. </summary>
    internal static string GetMileageUnitText(VehicleMileageUnit mileageUnit)
    {
        return MaintenanceDocumentFacts.GetMileageUnit(mileageUnit);
    }

    /// <summary> Добавляет шапку документа с заголовком, подзаголовком и датой. </summary>
    internal static void AddPageHeader(Body body, string title, string subtitle, string generatedDate)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { After = "80" },
                new Justification { Val = JustificationValues.Center }
            ),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontFamily, HighAnsi = FontFamily, ComplexScript = FontFamily },
                    new Bold(),
                    new Color { Val = PrimaryColor },
                    new FontSize { Val = "34" }
                ),
                new Text(title)
            )
        ));

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { After = "60" },
                    new Justification { Val = JustificationValues.Center }
                ),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = FontFamily, HighAnsi = FontFamily, ComplexScript = FontFamily },
                        new Color { Val = MutedColor },
                        new FontSize { Val = "22" }
                    ),
                    new Text(subtitle)
                )
            ));
        }

        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { After = "260" },
                new Justification { Val = JustificationValues.Center }
            ),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = FontFamily, HighAnsi = FontFamily, ComplexScript = FontFamily },
                    new Color { Val = MutedColor },
                    new FontSize { Val = "18" }
                ),
                new Text($"Сформировано: {generatedDate}")
            )
        ));
    }

    /// <summary> Добавляет пустой отступ после содержимого. </summary>
    internal static void AddSpacer(Body body, int after)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = after.ToString() }),
            new Run(new Text(string.Empty))
        ));
    }

    /// <summary> Добавляет панель с датой формирования и признаком фотографий. </summary>
    internal static void AddGenerationPanel(Body body, string generatedDate, bool includePhotos, string projectName)
    {
        var table = WordTableComponents.CreateInfoTable();
        WordTableComponents.AddInfoRow(table, "Дата формирования", generatedDate);
        WordTableComponents.AddInfoRow(table, "Система", projectName);
        WordTableComponents.AddInfoRow(table, "Фотографии", includePhotos ? "Включены в документ" : "Не включены");
        body.AppendChild(table);
        AddSpacer(body, 180);
    }

    /// <summary> Возвращает заголовок техники или плейсхолдер, если данных нет. </summary>
    internal static string GetVehicleTitle(WordVehicleInfo? vehicle)
    {
        if (vehicle == null) return string.Empty;

        return MaintenanceDocumentFacts.GetVehicleTitle(vehicle.BrandName, vehicle.ModelName, vehicle.Name, vehicle.RegistrationPlate);
    }

    /// <summary> Возвращает текстовое описание типа обслуживания по коду. </summary>
    internal static string GetMaintenanceTypeText(int type)
    {
        return MaintenanceDocumentFacts.GetMaintenanceTypeText(type);
    }

    /// <summary> Добавляет текст подвала в документ. </summary>
    internal static void AddFooter(Body body, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new ParagraphBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 8, Color = HeaderFill }
                ),
                new SpacingBetweenLines { Before = "260", After = "0" }
            ),
            new Run(
                new RunProperties(
                    new Color { Val = MutedColor },
                    new FontSize { Val = "18" }
                ),
                new Text(text)
            )
        ));
    }

    /// <summary> Добавляет блок статистики с количеством и суммой. </summary>
    internal static void AddStatistics(Body body, int totalCount, decimal totalSpent)
    {
        var table = WordTableComponents.CreateInfoTable();
        WordTableComponents.AddInfoRow(table, "Всего записей обслуживания", totalCount.ToString());
        WordTableComponents.AddInfoRow(table, "Общие затраты", FormatMoney(totalSpent));
        body.AppendChild(table);
        AddSpacer(body, 120);
    }

    /// <summary> Форматирует денежное значение для отображения. </summary>
    internal static string FormatMoney(decimal value)
    {
        return $"{value:N2} руб.";
    }

    /// <summary> Применяет настройки шрифта по умолчанию к документу. </summary>
    internal static void AddDocumentDefaults(MainDocumentPart mainPart)
    {
        var styleDefinitionsPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        styleDefinitionsPart.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(
                    new RunPropertiesBaseStyle(
                        new RunFonts { Ascii = FontFamily, HighAnsi = FontFamily, ComplexScript = FontFamily },
                        new Color { Val = "101828" },
                        new FontSize { Val = "21" }
                    )
                ),
                new ParagraphPropertiesDefault(
                    new ParagraphPropertiesBaseStyle(
                        new SpacingBetweenLines { After = "120", Line = "260", LineRule = LineSpacingRuleValues.Auto }
                    )
                )
            )
        );
        styleDefinitionsPart.Styles.Save();
    }

    /// <summary> Добавляет блок информации об обслуживании в документ. </summary>
    internal static void AddMaintenanceInfo(Body body, WordMaintenanceInfo maintenance, VehicleMileageUnit mileageUnit)
    {
        if (maintenance == null)
        {
            AddEmptyState(body, "Данные обслуживания отсутствуют");
            return;
        }

        var table = WordTableComponents.CreateInfoTable();
        WordTableComponents.AddInfoRow(table, "Тип", GetMaintenanceTypeText(maintenance.Type));
        WordTableComponents.AddInfoRow(table, "Дата", maintenance.Date);

        if (maintenance.Mileage.HasValue)
        {
            WordTableComponents.AddInfoRow(table, GetMileageLabel(mileageUnit).TrimEnd(':'), $"{maintenance.Mileage.Value:N0} {GetMileageUnitText(mileageUnit)}");
        }

        if (!string.IsNullOrWhiteSpace(maintenance.Location))
        {
            WordTableComponents.AddInfoRow(table, "Место", maintenance.Location);
        }

        if (!string.IsNullOrWhiteSpace(maintenance.Performer))
        {
            WordTableComponents.AddInfoRow(table, "Исполнитель", maintenance.Performer);
        }

        if (maintenance.Price.HasValue && maintenance.Price > 0)
        {
            WordTableComponents.AddInfoRow(table, "Общая стоимость", FormatMoney(maintenance.Price.Value));
        }

        body.AppendChild(table);
        AddSpacer(body, 120);
    }

    /// <summary> Добавляет сообщение о пустом состоянии в документ. </summary>
    internal static void AddEmptyState(Body body, string message)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new Shading { Val = ShadingPatternValues.Clear, Fill = SectionFill },
                new SpacingBetweenLines { Before = "80", After = "120" },
                new Justification { Val = JustificationValues.Center }
            ),
            new Run(
                new RunProperties(new Italic(), new Color { Val = MutedColor }),
                new Text(message)
            )
        ));
    }

    /// <summary> Добавляет свойства раздела в тело документа. </summary>
    internal static void AddSectionProperties(Body body)
    {
        body.AppendChild(new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 },
            new PageMargin
            {
                Top = 1000,
                Right = 900,
                Bottom = 900,
                Left = 900,
                Header = 450,
                Footer = 450,
                Gutter = 0
            }
        ));
    }
}
