using OpenCRM.Modules.Documents.Templates;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Рендерер PDF-заказов-нарядов и актов обслуживания. </summary>
internal sealed class MaintenanceOrderPdfRenderer(string projectName)
{
    /// <summary> Формирует строки исполнителя для документа. </summary>
    private static IReadOnlyList<string> GetExecutorLines(MaintenanceWorkActPdfModel model)
    {
        var lines = new List<string>();

        var taxParts = new[]
        {
            string.IsNullOrWhiteSpace(model.ExecutorInn) ? null : $"ИНН {model.ExecutorInn}",
            string.IsNullOrWhiteSpace(model.ExecutorKpp) ? null : $"КПП {model.ExecutorKpp}",
            string.IsNullOrWhiteSpace(model.ExecutorOgrn) ? null : $"ОГРН {model.ExecutorOgrn}"
        }.Where(x => !string.IsNullOrWhiteSpace(x));

        var taxLine = string.Join(", ", taxParts);
        if (!string.IsNullOrWhiteSpace(taxLine)) lines.Add(taxLine);
        if (!string.IsNullOrWhiteSpace(model.ExecutorAddress)) lines.Add(model.ExecutorAddress);
        if (!string.IsNullOrWhiteSpace(model.ExecutorPhone)) lines.Add($"Тел.: {model.ExecutorPhone}");
        if (!string.IsNullOrWhiteSpace(model.ExecutorEmail)) lines.Add($"Email: {model.ExecutorEmail}");
        if (!string.IsNullOrWhiteSpace(model.ExecutorSignatoryBasis)) lines.Add($"Основание: {model.ExecutorSignatoryBasis}");

        return lines;
    }

    /// <summary> Отрисовывает стоимость позиций, скидку и итог документа. </summary>
    private static void RenderSummary(IContainer container, string title, decimal grossAmount, decimal discountAmount, string currencyCode)
    {
        container.Element(PdfDocumentComponents.Card).Column(column =>
        {
            column.Spacing(5);
            column.Item().Row(row =>
            {
                row.RelativeItem().Text("Работы и материалы").FontSize(9).FontColor(PdfDocumentComponents.MutedColor);
                row.AutoItem().Text(PdfDocumentComponents.FormatMoney(grossAmount, currencyCode)).FontSize(9);
            });

            if (discountAmount > 0)
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("Скидка").FontSize(9).FontColor(PdfDocumentComponents.MutedColor);
                    row.AutoItem().Text($"-{PdfDocumentComponents.FormatMoney(discountAmount, currencyCode)}").FontSize(9);
                });
            }

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(title)
                    .FontSize(11)
                    .Bold()
                    .FontColor(PdfDocumentComponents.PrimaryColor);
                row.AutoItem().Text(PdfDocumentComponents.FormatMoney(Math.Max(0, grossAmount - discountAmount), currencyCode))
                    .FontSize(13)
                    .Bold()
                    .FontColor(PdfDocumentComponents.AccentColor);
            });
        });
    }

    /// <summary> Отрисовывает блок сведений об обслуживании в акте. </summary>
    private static void RenderWorkActMaintenanceInfo(
        IContainer container,
        MaintenanceWorkActPdfModel model,
        string dateLabel)
    {
        container.Element(PdfDocumentComponents.Card).Column(column =>
        {
            column.Spacing(10);
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Исполнитель", model.ExecutorName));
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderInlineWritableFact(c, "Заказчик", model.CustomerName));
            });

            var executorLines = GetExecutorLines(model);
            if (executorLines.Count > 0)
            {
                column.Item().Text(string.Join("; ", executorLines))
                    .FontSize(8.5f)
                    .FontColor(PdfDocumentComponents.MutedColor);
            }

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Техника", model.VehicleTitle));
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Тип обслуживания", model.MaintenanceType));
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, dateLabel, model.MaintenanceDate));
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, model.MileageLabel, model.Mileage.HasValue ? $"{model.Mileage.Value:n0} {model.MileageUnit}" : "-"));
            });

            if (!string.IsNullOrWhiteSpace(model.PlannedPeriod))
            {
                column.Item().Element(c => PdfDocumentComponents.RenderFact(c, "Плановый период", model.PlannedPeriod));
            }

            if (!string.IsNullOrWhiteSpace(model.VehicleRegistrationPlate) || !string.IsNullOrWhiteSpace(model.VehicleVin))
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Госномер", model.VehicleRegistrationPlate));
                    row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "VIN / серийный номер", model.VehicleVin));
                });
            }

            if (!string.IsNullOrWhiteSpace(model.VehicleYear) || !string.IsNullOrWhiteSpace(model.Location))
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Год выпуска", model.VehicleYear));
                    row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Место выполнения", model.Location));
                });
            }

            if (!string.IsNullOrWhiteSpace(model.Performer))
            {
                column.Item().Element(c => PdfDocumentComponents.RenderFact(c, "Ответственный исполнитель", model.Performer));
            }
        });
    }

    /// <summary> Генерирует PDF-заказ-наряд по обслуживанию. </summary>
    internal async Task<byte[]> GenerateMaintenanceWorkOrderPdfAsync(MaintenanceWorkActPdfModel model)
    {
        return await Task.Run(() =>
        {
            var preset = MaintenanceDocumentTemplateCatalog.Resolve(model.DocumentStyleCode, model.ScenarioCode);
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    PdfDocumentComponents.ConfigurePage(page);

                    page.Content().Column(column =>
                    {
                        column.Spacing(14);
                        column.Item().Element(c => PdfDocumentComponents.RenderScenarioDocumentHeader(c, preset, $"Заказ-наряд № {model.ActNumber}", model.ActDate, projectName));
                        column.Item().Text(preset.WorkOrderIntro).FontSize(10).FontColor(PdfDocumentComponents.TextColor);
                        column.Item().Element(c => PdfDocumentComponents.RenderSection(c, preset.OrderInfoTitle, section => RenderWorkActMaintenanceInfo(section, model, "Плановая дата")));

                        if (model.Works.Count > 0)
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, preset.PlannedWorksTitle, section => PdfTableComponents.RenderServices(section, model.Works, showTotal: false, currencyCode: model.CurrencyCode)));
                        }
                        else
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderEmptyState(c, "Работы и материалы не указаны"));
                        }

                        if (!string.IsNullOrWhiteSpace(model.Comment))
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Комментарий", section => PdfTableComponents.RenderComment(section, model.Comment)));
                        }

                        column.Item().Text(preset.WorkOrderNotice).FontSize(9).FontColor(PdfDocumentComponents.MutedColor);
                        column.Item().PaddingTop(12).Element(c => RenderSummary(
                            c,
                            preset.WorkOrderSummaryTitle,
                            model.Works.Sum(x => x.Price ?? 0),
                            model.DiscountAmount,
                            model.CurrencyCode));
                        column.Item().PaddingTop(18).Element(c => RenderSignatures(c, model));
                    });

                    page.Footer().Element(c => PdfDocumentComponents.RenderFooter(c, projectName));
                });
            });

            return document.GeneratePdf();
        });
    }

    internal async Task<byte[]> GenerateMaintenanceWorkActPdfAsync(MaintenanceWorkActPdfModel model)
    {
        return await Task.Run(() =>
        {
            var preset = MaintenanceDocumentTemplateCatalog.Resolve(model.DocumentStyleCode, model.ScenarioCode);
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    PdfDocumentComponents.ConfigurePage(page);

                    page.Content().Column(column =>
                    {
                        column.Spacing(14);
                        column.Item().Element(c => PdfDocumentComponents.RenderScenarioDocumentHeader(c, preset, $"Акт выполненных работ № {model.ActNumber}", model.ActDate, projectName));
                        column.Item().Text(preset.WorkActIntro).FontSize(10).FontColor(PdfDocumentComponents.TextColor);
                        column.Item().Element(c => PdfDocumentComponents.RenderSection(c, preset.MaintenanceInfoTitle, section => RenderWorkActMaintenanceInfo(section, model, "Дата выполнения")));

                        if (model.Works.Count > 0)
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, preset.CompletedWorksTitle, section => PdfTableComponents.RenderServices(section, model.Works, showTotal: false, currencyCode: model.CurrencyCode)));
                        }
                        else
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderEmptyState(c, "Работы и материалы не указаны"));
                        }

                        if (!string.IsNullOrWhiteSpace(model.Comment))
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Комментарий", section => PdfTableComponents.RenderComment(section, model.Comment)));
                        }

                        column.Item().PaddingTop(12).Element(c => RenderSummary(
                            c,
                            preset.ActSummaryTitle,
                            model.Works.Sum(x => x.Price ?? 0),
                            model.DiscountAmount,
                            model.CurrencyCode));
                        column.Item().PaddingTop(18).Element(c => RenderSignatures(c, model));
                    });

                    page.Footer().Element(c => PdfDocumentComponents.RenderFooter(c, projectName));
                });
            });

            return document.GeneratePdf();
        });
    }

    /// <summary> Отрисовывает блок подписей в документе. </summary>
    private static void RenderSignatures(IContainer container, MaintenanceWorkActPdfModel model)
    {
        container.EnsureSpace(110).Row(row =>
        {
            row.RelativeItem().Element(c => PdfDocumentComponents.RenderSignatureBlock(c, "Исполнитель", model.ExecutorSignatoryPosition, model.ExecutorSignatoryName));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => PdfDocumentComponents.RenderSignatureBlock(c, "Заказчик", string.Empty, model.CustomerName));
        });
    }

}
