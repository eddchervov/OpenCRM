using OpenCRM.Modules.Documents.Templates;
using OrganizationScenarioDocumentKindCodes = OpenCRM.Domain.OrganizationScenarioDocumentKindCodes;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Формирует PDF-документ обслуживания по шаблону сценария. </summary>
internal sealed class ScenarioMaintenancePdfRenderer(string projectName)
{
    /// <summary> Отрисовывает блок подписей для документа сценария. </summary>
    private static void RenderScenarioSignatures(IContainer container, ScenarioMaintenanceDocumentPdfModel model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => PdfDocumentComponents.RenderSignatureBlock(c, "Исполнитель", model.ExecutorSignatoryPosition, model.ExecutorSignatoryName));
            row.ConstantItem(24);
            row.RelativeItem().Element(c => PdfDocumentComponents.RenderSignatureBlock(c, "Клиент / ответственный", string.Empty, model.CustomerName));
        });
    }

    /// <summary> Отрисовывает блок готовности по данным сценария. </summary>
    private static void RenderReadinessBlock(IContainer container, ScenarioMaintenanceDocumentPdfModel model)
    {
        RenderBulletList(container,
        [
            "Работы зафиксированы в карточке обслуживания.",
            "Ответственный может вернуть технику в работу после проверки.",
            string.IsNullOrWhiteSpace(model.Comment) ? "Дополнительные замечания не указаны." : "Дополнительные замечания указаны в комментарии."
        ]);
    }

    /// <summary> Отрисовывает блок чек-листа сценария. </summary>
    private static void RenderChecklistBlock(IContainer container, ScenarioMaintenanceDocumentPdfModel model)
    {
        RenderBulletList(container,
        [
            "Проверить состояние техники перед эксплуатацией.",
            "Проверить открытые замечания и связанные неисправности.",
            "Проверить документы, фото и комментарии в карточке работы.",
            "Зафиксировать решение ответственного после осмотра."
        ]);
    }

    /// <summary> Отрисовывает финансовый блок документа сценария. </summary>
    private static void RenderScenarioFinance(IContainer container, ScenarioMaintenanceDocumentPdfModel model)
    {
        var kindCode = OrganizationScenarioDocumentKindCodes.Normalize(model.DocumentKindCode) ?? string.Empty;
        var netAmount = Math.Max(0, model.GrossAmount - model.DiscountAmount);

        container.Element(PdfDocumentComponents.Card).Column(column =>
        {
            column.Spacing(8);
            column.Item().Text(GetScenarioFinanceTitle(kindCode))
                .FontSize(11)
                .Bold()
                .FontColor(PdfDocumentComponents.PrimaryColor);

            if (kindCode == OrganizationScenarioDocumentKindCodes.WorkOrder)
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Предварительная сумма", PdfDocumentComponents.FormatMoney(model.GrossAmount)));
                    if (model.DiscountAmount > 0)
                    {
                        row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Скидка", PdfDocumentComponents.FormatMoney(model.DiscountAmount)));
                    }
                    row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Плановая стоимость", PdfDocumentComponents.FormatMoney(netAmount)));
                });

                return;
            }

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Сумма", PdfDocumentComponents.FormatMoney(model.GrossAmount)));
                if (model.DiscountAmount > 0)
                {
                    row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Скидка", PdfDocumentComponents.FormatMoney(model.DiscountAmount)));
                }
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Итого к оплате", PdfDocumentComponents.FormatMoney(netAmount)));
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Оплачено", PdfDocumentComponents.FormatMoney(model.PaidAmount)));
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Остаток", PdfDocumentComponents.FormatMoney(model.DebtAmount)));
            });
        });
    }

    /// <summary> Определяет, нужно ли отображать финансовый блок для кода документа. </summary>
    private static bool ShouldRenderFinance(string kindCode)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(kindCode) is not OrganizationScenarioDocumentKindCodes.PhotoReport
            and not OrganizationScenarioDocumentKindCodes.InspectionAct
            and not OrganizationScenarioDocumentKindCodes.Recommendations
            and not OrganizationScenarioDocumentKindCodes.ReadinessReport
            and not OrganizationScenarioDocumentKindCodes.Checklist;
    }

    /// <summary> Отрисовывает маркированный список пунктов. </summary>
    private static void RenderBulletList(IContainer container, IReadOnlyList<string> items)
    {
        container.Element(PdfDocumentComponents.Card).Column(column =>
        {
            column.Spacing(5);
            foreach (var item in items)
            {
                column.Item().Text($"- {item}")
                    .FontSize(10)
                    .FontColor(PdfDocumentComponents.TextColor);
            }
        });
    }

    /// <summary> Возвращает вводный текст документа сценария по его коду и шаблону. </summary>
    private static string GetScenarioDocumentIntro(string kindCode, MaintenanceDocumentTemplate preset, string documentTitle)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(kindCode) switch
        {
            OrganizationScenarioDocumentKindCodes.WorkOrder => preset.WorkOrderIntro,
            OrganizationScenarioDocumentKindCodes.WorkAct => preset.WorkActIntro,
            OrganizationScenarioDocumentKindCodes.InspectionAct => "Документ фиксирует состояние техники, замечания, фотографии и исходные данные перед выполнением работ.",
            OrganizationScenarioDocumentKindCodes.PhotoReport => "Документ собирает фотографии проекта, выполненные работы, материалы и комментарии без разделения на этапы до/после.",
            OrganizationScenarioDocumentKindCodes.Recommendations => "Документ передает клиенту рекомендации по дальнейшим действиям, уходу или следующему визиту на основе текущей записи.",
            OrganizationScenarioDocumentKindCodes.MaterialsSpec => "Документ фиксирует материалы, расходники, стоимость и связанные позиции работы.",
            OrganizationScenarioDocumentKindCodes.Receipt => "Документ подтверждает оказанную услугу, состав визита и итоговую стоимость.",
            OrganizationScenarioDocumentKindCodes.ReadinessReport => "Документ фиксирует готовность техники к дальнейшей эксплуатации или занятиям.",
            OrganizationScenarioDocumentKindCodes.Checklist => "Документ помогает пройти контрольный список осмотра и замечаний.",
            _ => $"Документ \"{documentTitle}\" сформирован по данным карточки работы."
        };
    }

    /// <summary> Формирует PDF-документ обслуживания по данным сценария. </summary>
    internal async Task<byte[]> GenerateScenarioMaintenanceDocumentPdfAsync(ScenarioMaintenanceDocumentPdfModel model)
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
                        var title = string.IsNullOrWhiteSpace(model.DocumentNumber)
                            ? model.DocumentTitle
                            : $"{model.DocumentTitle} № {model.DocumentNumber}";
                        column.Item().Element(c => PdfDocumentComponents.RenderScenarioDocumentHeader(c, preset, title, model.DocumentDate, projectName));
                        column.Item().Text(GetScenarioDocumentIntro(model.DocumentKindCode, preset, model.DocumentTitle))
                            .FontSize(10)
                            .FontColor(PdfDocumentComponents.TextColor);

                        column.Item().Element(c => PdfDocumentComponents.RenderSection(c, GetScenarioDocumentInfoTitle(model.DocumentKindCode, preset), section => RenderScenarioMaintenanceInfo(section, model)));

                        if (model.Works.Count > 0)
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, GetScenarioWorksTitle(model.DocumentKindCode, preset), section => PdfTableComponents.RenderServices(section, model.Works)));
                        }
                        else
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderEmptyState(c, "Работы, услуги и материалы не указаны"));
                        }

                        RenderScenarioSpecificSections(column, model);

                        if (!string.IsNullOrWhiteSpace(model.Comment))
                        {
                            column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Комментарий", section => PdfTableComponents.RenderComment(section, model.Comment)));
                        }

                        if (ShouldRenderFinance(model.DocumentKindCode))
                        {
                            column.Item().Element(c => RenderScenarioFinance(c, model));
                        }

                        if (ShouldRenderSignatures(model.DocumentKindCode))
                        {
                            column.Item().PaddingTop(18).Element(c => RenderScenarioSignatures(c, model));
                        }
                    });

                    page.Footer().Element(c => PdfDocumentComponents.RenderFooter(c, projectName));
                });
            });

            return document.GeneratePdf();
        });
    }

    /// <summary> Возвращает заголовок информационного блока документа сценария. </summary>
    private static string GetScenarioDocumentInfoTitle(string kindCode, MaintenanceDocumentTemplate preset)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(kindCode) switch
        {
            OrganizationScenarioDocumentKindCodes.WorkOrder => preset.OrderInfoTitle,
            _ => preset.MaintenanceInfoTitle
        };
    }

    /// <summary> Возвращает заголовок блока работ документа сценария. </summary>
    private static string GetScenarioWorksTitle(string kindCode, MaintenanceDocumentTemplate preset)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(kindCode) switch
        {
            OrganizationScenarioDocumentKindCodes.WorkOrder => preset.PlannedWorksTitle,
            OrganizationScenarioDocumentKindCodes.MaterialsSpec => "Материалы, расходники и позиции",
            OrganizationScenarioDocumentKindCodes.Recommendations => "Основание рекомендаций",
            OrganizationScenarioDocumentKindCodes.PhotoReport => "Работы и материалы проекта",
            _ => preset.CompletedWorksTitle
        };
    }

    /// <summary> Отрисовывает специфичные для сценария разделы документа. </summary>
    private static void RenderScenarioSpecificSections(ColumnDescriptor column, ScenarioMaintenanceDocumentPdfModel model)
    {
        switch (OrganizationScenarioDocumentKindCodes.Normalize(model.DocumentKindCode))
        {
            case OrganizationScenarioDocumentKindCodes.PhotoReport:
                column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Фотографии проекта", section => PdfPhotoComponents.RenderPhotos(section, model.Photos)));
                break;
            case OrganizationScenarioDocumentKindCodes.InspectionAct:
                column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Осмотр и зафиксированные материалы", section =>
                {
                    if (model.Photos.Count > 0)
                    {
                        PdfPhotoComponents.RenderPhotos(section, model.Photos);
                    }
                    else
                    {
                        PdfDocumentComponents.RenderEmptyState(section, "Фотографии осмотра не загружены");
                    }
                }));
                break;
            case OrganizationScenarioDocumentKindCodes.Recommendations:
                column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Рекомендации клиенту", section => RenderRecommendationBlock(section, model)));
                break;
            case OrganizationScenarioDocumentKindCodes.MaterialsSpec:
                column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Спецификация материалов и затрат", section => RenderMaterialSpecBlock(section, model)));
                break;
            case OrganizationScenarioDocumentKindCodes.ReadinessReport:
                column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Готовность к эксплуатации", section => RenderReadinessBlock(section, model)));
                break;
            case OrganizationScenarioDocumentKindCodes.Checklist:
                column.Item().Element(c => PdfDocumentComponents.RenderSection(c, "Контрольный список", section => RenderChecklistBlock(section, model)));
                break;
        }
    }

    /// <summary> Отрисовывает блок рекомендаций сценария. </summary>
    private static void RenderRecommendationBlock(IContainer container, ScenarioMaintenanceDocumentPdfModel model)
    {
        var recommendations = new List<string>();
        if (model.Works.Count > 0)
        {
            recommendations.Add("Проверьте рекомендации мастера по выполненным работам и материалам.");
        }

        if (!string.IsNullOrWhiteSpace(model.Comment))
        {
            recommendations.Add("Основной комментарий мастера указан в карточке работы и продублирован в документе.");
        }

        recommendations.Add(model.Mileage.HasValue
            ? $"Следующий контроль удобно запланировать от текущего значения: {model.Mileage.Value:n0} {model.MileageUnit}."
            : "Следующий контроль можно запланировать по дате или после появления новых замечаний.");

        RenderBulletList(container, recommendations);
    }

    /// <summary> Отрисовывает блок спецификации материалов сценария. </summary>
    private static void RenderMaterialSpecBlock(IContainer container, ScenarioMaintenanceDocumentPdfModel model)
    {
        var items = new List<string>
        {
            $"Сумма материалов и работ по позициям: {PdfDocumentComponents.FormatMoney(model.Works.Sum(x => x.Price ?? 0))}.",
            "Материалы и работы перечислены по позициям для согласования с заказчиком.",
            "Замены материалов согласуются с заказчиком до выполнения работ."
        };
        RenderBulletList(container, items);
    }

    /// <summary> Возвращает заголовок финансового блока по коду документа. </summary>
    private static string GetScenarioFinanceTitle(string kindCode)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(kindCode) switch
        {
            OrganizationScenarioDocumentKindCodes.WorkOrder => "Плановая стоимость",
            OrganizationScenarioDocumentKindCodes.Receipt => "Оплата услуги",
            OrganizationScenarioDocumentKindCodes.MaterialsSpec => "Итоги по материалам",
            _ => "Стоимость работ"
        };
    }

    /// <summary> Определяет, нужно ли отображать подписи для кода документа. </summary>
    private static bool ShouldRenderSignatures(string kindCode)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(kindCode) is
            OrganizationScenarioDocumentKindCodes.WorkAct or
            OrganizationScenarioDocumentKindCodes.WorkOrder or
            OrganizationScenarioDocumentKindCodes.InspectionAct or
            OrganizationScenarioDocumentKindCodes.Receipt or
            OrganizationScenarioDocumentKindCodes.ReadinessReport;
    }

    /// <summary> Отрисовывает информационный блок обслуживания сценария. </summary>
    private static void RenderScenarioMaintenanceInfo(IContainer container, ScenarioMaintenanceDocumentPdfModel model)
    {
        container.Element(PdfDocumentComponents.Card).Column(column =>
        {
            column.Spacing(10);
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Исполнитель", model.ExecutorName));
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderInlineWritableFact(c, "Клиент / заказчик", model.CustomerName));
            });

            var executorLines = new[] { model.ExecutorInn, model.ExecutorKpp, model.ExecutorOgrn, model.ExecutorAddress, model.ExecutorPhone, model.ExecutorEmail }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (executorLines.Count > 0)
            {
                column.Item().Text(string.Join("; ", executorLines))
                    .FontSize(8.5f)
                    .FontColor(PdfDocumentComponents.MutedColor);
            }

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Техника", model.VehicleTitle));
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Тип записи", model.MaintenanceType));
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Дата", model.MaintenanceDate));
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
                    row.RelativeItem().Element(c => PdfDocumentComponents.RenderFact(c, "Место", model.Location));
                });
            }

            if (!string.IsNullOrWhiteSpace(model.Performer))
            {
                column.Item().Element(c => PdfDocumentComponents.RenderFact(c, "Ответственный", model.Performer));
            }
        });
    }
}
