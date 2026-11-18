using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenCRM.Modules.Documents.Templates;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents.Word.Rendering;

/// <summary> Отрисовывает заказ-наряды и акты работ в Word. </summary>
internal sealed class MaintenanceOrderDocxRenderer(string projectName)
{
    /// <summary> Формирует Word-заказ-наряд по модели. </summary>
    internal Task<byte[]> GenerateMaintenanceWorkOrderDocxAsync(WordMaintenanceWorkActModel model)
    {
        using var memoryStream = new MemoryStream();

        using (var wordDocument = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            WordDocumentComponents.AddDocumentDefaults(mainPart);

            var body = mainPart.Document.AppendChild(new Body());
            var preset = MaintenanceDocumentTemplateCatalog.Resolve(model.DocumentStyleCode, model.ScenarioCode);
            WordDocumentComponents.AddPageHeader(body, $"Заказ-наряд № {model.ActNumber}", preset.Subtitle, model.GeneratedDate);
            WordDocumentComponents.AddScenarioIntro(body, preset.Label, preset.WorkOrderIntro, preset.AccentColorWord);

            WordDocumentComponents.AddSectionHeading(body, preset.OrderInfoTitle);
            AddWorkActInfo(body, model, isWorkOrder: true);

            WordDocumentComponents.AddSectionHeading(body, preset.PlannedWorksTitle);
            if (model.Works.Any())
            {
                WordTableComponents.AddServicesTable(body, model.Works, showTotal: false, currencyCode: model.CurrencyCode);
            }
            else
            {
                WordDocumentComponents.AddEmptyState(body, "Работы и материалы не указаны");
            }

            AddWorkSummary(body, model, preset.WorkOrderSummaryTitle);

            if (!string.IsNullOrWhiteSpace(model.Comment))
            {
                WordDocumentComponents.AddComment(body, model.Comment);
            }

            AddWorkOrderNotice(body, preset.WorkOrderNotice);
            AddWorkActSignatures(body, model);
            WordDocumentComponents.AddFooter(body, $"Документ сформирован в {projectName}");
            WordDocumentComponents.AddSectionProperties(body);
            mainPart.Document.Save();
        }

        return Task.FromResult(memoryStream.ToArray());
    }

    /// <summary> Добавляет блок подписей в акт выполненных работ. </summary>
    private static void AddWorkActSignatures(Body body, WordMaintenanceWorkActModel model)
    {
        WordDocumentComponents.AddSectionHeading(body, "Подписи сторон");

        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            WordTableComponents.CreateTableBorders("FFFFFF", BorderValues.None, 0),
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "240", Type = TableWidthUnitValues.Dxa }
            )
        ));

        table.AppendChild(new TableGrid(
            new GridColumn { Width = "4000" },
            new GridColumn { Width = "4000" }
        ));

        var headerRow = table.AppendChild(new TableRow());
        WordTableComponents.AddTableCell(headerRow, "Исполнитель", "4000", JustificationValues.Left, "FFFFFF", bold: true, color: WordDocumentComponents.PrimaryColor);
        WordTableComponents.AddTableCell(headerRow, "Заказчик", "4000", JustificationValues.Left, "FFFFFF", bold: true, color: WordDocumentComponents.PrimaryColor);

        var signatory = string.IsNullOrWhiteSpace(model.ExecutorSignatoryName)
            ? model.ExecutorName
            : model.ExecutorSignatoryName;

        var basis = string.IsNullOrWhiteSpace(model.ExecutorSignatoryBasis)
            ? string.Empty
            : $" ({model.ExecutorSignatoryBasis})";

        var signatureRow = table.AppendChild(new TableRow());
        WordTableComponents.AddTableCell(signatureRow, $"________________ / {signatory}{basis}", "4000", JustificationValues.Left, "FFFFFF");
        WordTableComponents.AddTableCell(signatureRow, "________________ / __________________", "4000", JustificationValues.Left, "FFFFFF");

        body.AppendChild(table);
        WordDocumentComponents.AddSpacer(body, 120);
    }

    /// <summary> Формирует Word-акт выполненных работ по модели. </summary>
    internal Task<byte[]> GenerateMaintenanceWorkActDocxAsync(WordMaintenanceWorkActModel model)
    {
        using var memoryStream = new MemoryStream();

        using (var wordDocument = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            WordDocumentComponents.AddDocumentDefaults(mainPart);

            var body = mainPart.Document.AppendChild(new Body());
            var preset = MaintenanceDocumentTemplateCatalog.Resolve(model.DocumentStyleCode, model.ScenarioCode);
            WordDocumentComponents.AddPageHeader(body, $"Акт выполненных работ № {model.ActNumber}", preset.Subtitle, model.GeneratedDate);
            WordDocumentComponents.AddScenarioIntro(body, preset.Label, preset.WorkActIntro, preset.AccentColorWord);

            WordDocumentComponents.AddSectionHeading(body, preset.MaintenanceInfoTitle);
            AddWorkActInfo(body, model, isWorkOrder: false);

            WordDocumentComponents.AddSectionHeading(body, preset.CompletedWorksTitle);
            if (model.Works.Any())
            {
                WordTableComponents.AddServicesTable(body, model.Works, showTotal: false, currencyCode: model.CurrencyCode);
            }
            else
            {
                WordDocumentComponents.AddEmptyState(body, "Работы и материалы не указаны");
            }

            AddWorkSummary(body, model, preset.ActSummaryTitle);

            if (!string.IsNullOrWhiteSpace(model.Comment))
            {
                WordDocumentComponents.AddComment(body, model.Comment);
            }

            AddWorkActSignatures(body, model);
            WordDocumentComponents.AddFooter(body, $"Документ сформирован в {projectName}");
            WordDocumentComponents.AddSectionProperties(body);
            mainPart.Document.Save();
        }

        return Task.FromResult(memoryStream.ToArray());
    }

    /// <summary> Возвращает строку с описанием исполнителя работ. </summary>
    private static string FormatExecutor(WordMaintenanceWorkActModel model)
    {
        var requisites = new[]
        {
            model.ExecutorName,
            WordDocumentComponents.FormatOptional("ИНН", model.ExecutorInn),
            WordDocumentComponents.FormatOptional("КПП", model.ExecutorKpp),
            WordDocumentComponents.FormatOptional("ОГРН", model.ExecutorOgrn),
            model.ExecutorAddress,
            model.ExecutorPhone,
            model.ExecutorEmail
        }.Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join(", ", requisites);
    }

    /// <summary> Добавляет информационный блок в акт выполненных работ. </summary>
    private static void AddWorkActInfo(Body body, WordMaintenanceWorkActModel model, bool isWorkOrder)
    {
        var table = WordTableComponents.CreateInfoTable();

        WordTableComponents.AddInfoRow(table, isWorkOrder ? "Дата заказ-наряда" : "Дата акта", model.ActDate);
        WordTableComponents.AddInfoRow(table, "Исполнитель", FormatExecutor(model));
        WordTableComponents.AddInfoRow(table, "Заказчик", string.IsNullOrWhiteSpace(model.CustomerName) ? "______________________________" : model.CustomerName);
        WordTableComponents.AddInfoRow(table, "Техника", model.VehicleTitle);

        if (!string.IsNullOrWhiteSpace(model.VehicleRegistrationPlate))
        {
            WordTableComponents.AddInfoRow(table, "Госномер", model.VehicleRegistrationPlate);
        }

        if (!string.IsNullOrWhiteSpace(model.VehicleVin))
        {
            WordTableComponents.AddInfoRow(table, "VIN", model.VehicleVin);
        }

        if (!string.IsNullOrWhiteSpace(model.VehicleYear))
        {
            WordTableComponents.AddInfoRow(table, "Год выпуска", model.VehicleYear);
        }

        WordTableComponents.AddInfoRow(table, "Тип обслуживания", model.MaintenanceType);
        WordTableComponents.AddInfoRow(table, isWorkOrder ? "Плановая дата" : "Дата выполнения", model.MaintenanceDate);
        if (!string.IsNullOrWhiteSpace(model.PlannedPeriod))
        {
            WordTableComponents.AddInfoRow(table, "Плановый период", model.PlannedPeriod);
        }

        if (model.Mileage.HasValue)
        {
            WordTableComponents.AddInfoRow(table, model.MileageLabel, $"{model.Mileage.Value:N0} {model.MileageUnit}");
        }

        if (!string.IsNullOrWhiteSpace(model.Location))
        {
            WordTableComponents.AddInfoRow(table, "Место", model.Location);
        }

        if (!string.IsNullOrWhiteSpace(model.Performer))
        {
            WordTableComponents.AddInfoRow(table, "Ответственный", model.Performer);
        }

        body.AppendChild(table);
        WordDocumentComponents.AddSpacer(body, 160);
    }

    /// <summary> Добавляет примечание к заказ-наряду. </summary>
    private static void AddWorkOrderNotice(Body body, string notice)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "120", After = "120" }),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = WordDocumentComponents.FontFamily, HighAnsi = WordDocumentComponents.FontFamily, ComplexScript = WordDocumentComponents.FontFamily },
                    new Color { Val = WordDocumentComponents.MutedColor },
                    new FontSize { Val = "20" }
                ),
                new Text(notice)
            )
        ));
    }

    /// <summary> Добавляет стоимость работ, скидку и итоговую сумму документа. </summary>
    private static void AddWorkSummary(Body body, WordMaintenanceWorkActModel model, string totalLabel)
    {
        var grossAmount = model.Works.Sum(work => work.Price ?? 0);
        var table = WordTableComponents.CreateInfoTable();
        WordTableComponents.AddInfoRow(table, "Работы и материалы", WordDocumentComponents.FormatMoney(grossAmount, model.CurrencyCode));

        if (model.DiscountAmount > 0)
        {
            WordTableComponents.AddInfoRow(table, "Скидка", $"-{WordDocumentComponents.FormatMoney(model.DiscountAmount, model.CurrencyCode)}");
        }

        WordTableComponents.AddInfoRow(
            table,
            totalLabel,
            WordDocumentComponents.FormatMoney(Math.Max(0, grossAmount - model.DiscountAmount), model.CurrencyCode));
        body.AppendChild(table);
        WordDocumentComponents.AddSpacer(body, 160);
    }
}
