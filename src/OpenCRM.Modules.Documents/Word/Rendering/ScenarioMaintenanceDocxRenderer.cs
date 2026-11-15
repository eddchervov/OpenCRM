using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenCRM.Domain;
using OpenCRM.Modules.Documents.Templates;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents.Word.Rendering;

/// <summary> Отрисовывает Word-документы обслуживания по шаблонам сценариев. </summary>
internal sealed class ScenarioMaintenanceDocxRenderer(string projectName)
{
    /// <summary> Формирует Word-документ обслуживания по данным сценария. </summary>
    internal async Task<byte[]> GenerateScenarioMaintenanceDocumentDocxAsync(WordScenarioMaintenanceDocumentModel model)
    {
        using var memoryStream = new MemoryStream();

        using (var wordDocument = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            WordDocumentComponents.AddDocumentDefaults(mainPart);

            var body = mainPart.Document.AppendChild(new Body());
            var preset = MaintenanceDocumentTemplateCatalog.Resolve(model.DocumentStyleCode, model.ScenarioCode);
            var title = string.IsNullOrWhiteSpace(model.DocumentNumber)
                ? model.DocumentTitle
                : $"{model.DocumentTitle} № {model.DocumentNumber}";

            WordDocumentComponents.AddPageHeader(body, title, preset.Subtitle, model.GeneratedDate);
            WordDocumentComponents.AddScenarioIntro(body, preset.Label, GetScenarioDocumentIntro(model.DocumentKindCode, preset, model.DocumentTitle), preset.AccentColorWord);

            WordDocumentComponents.AddSectionHeading(body, GetScenarioDocumentInfoTitle(model.DocumentKindCode, preset));
            AddScenarioDocumentInfo(body, model);

            WordDocumentComponents.AddSectionHeading(body, GetScenarioWorksTitle(model.DocumentKindCode, preset));
            if (model.Works.Any())
            {
                WordTableComponents.AddServicesTable(body, model.Works);
            }
            else
            {
                WordDocumentComponents.AddEmptyState(body, "Работы, услуги и материалы не указаны");
            }

            await AddScenarioSpecificSections(body, model, wordDocument);

            if (!string.IsNullOrWhiteSpace(model.Comment))
            {
                WordDocumentComponents.AddComment(body, model.Comment);
            }

            if (ShouldRenderFinance(model.DocumentKindCode))
            {
                AddScenarioFinance(body, model);
            }

            if (ShouldRenderSignatures(model.DocumentKindCode))
            {
                AddScenarioSignatures(body, model);
            }

            WordDocumentComponents.AddFooter(body, $"Документ сформирован в {projectName}");
            WordDocumentComponents.AddSectionProperties(body);
            mainPart.Document.Save();
        }

        return memoryStream.ToArray();
    }

    /// <summary> Добавляет информационный блок документа сценария. </summary>
    private static void AddScenarioDocumentInfo(Body body, WordScenarioMaintenanceDocumentModel model)
    {
        var table = WordTableComponents.CreateInfoTable();

        WordTableComponents.AddInfoRow(table, "Дата документа", model.DocumentDate);
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
        WordTableComponents.AddInfoRow(table, "Дата обслуживания", model.MaintenanceDate);
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

    /// <summary> Определяет, нужно ли отображать финансовый блок для кода документа. </summary>
    private static bool ShouldRenderFinance(string documentKindCode)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(documentKindCode) is not OrganizationScenarioDocumentKindCodes.PhotoReport
            and not OrganizationScenarioDocumentKindCodes.InspectionAct
            and not OrganizationScenarioDocumentKindCodes.Recommendations
            and not OrganizationScenarioDocumentKindCodes.ReadinessReport
            and not OrganizationScenarioDocumentKindCodes.Checklist;
    }

    /// <summary> Возвращает текстовое описание статуса оплаты. </summary>
    private static string GetPaymentStatusText(string? status)
    {
        return status switch
        {
            "paid" => "Оплачено",
            "partial" => "Частично оплачено",
            "overpaid" => "Переплата",
            _ => "Не оплачено"
        };
    }

    /// <summary> Возвращает строку с описанием исполнителя работ. </summary>
    private static string FormatExecutor(WordScenarioMaintenanceDocumentModel model)
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

    /// <summary> Возвращает вводный текст документа сценария по его коду и шаблону. </summary>
    private static string GetScenarioDocumentIntro(string documentKindCode, MaintenanceDocumentTemplate preset, string documentTitle)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(documentKindCode) switch
        {
            OrganizationScenarioDocumentKindCodes.WorkOrder => preset.WorkOrderIntro,
            OrganizationScenarioDocumentKindCodes.WorkAct => preset.WorkActIntro,
            OrganizationScenarioDocumentKindCodes.PhotoReport => "Документ фиксирует ход и результат работ с фотографиями по выбранной записи.",
            OrganizationScenarioDocumentKindCodes.InspectionAct => "Документ фиксирует состояние техники, замечания и фотофиксацию на момент осмотра.",
            OrganizationScenarioDocumentKindCodes.Recommendations => "Документ передает клиенту понятные рекомендации после выполненных работ.",
            OrganizationScenarioDocumentKindCodes.MaterialsSpec => "Документ собирает работы, материалы и финансовые показатели по записи.",
            OrganizationScenarioDocumentKindCodes.Receipt => "Документ подтверждает оказанную услугу и сумму по записи.",
            OrganizationScenarioDocumentKindCodes.ReadinessReport => "Документ подтверждает готовность техники и итоговые замечания.",
            OrganizationScenarioDocumentKindCodes.Checklist => "Документ помогает пройти контрольные пункты по записи обслуживания.",
            _ => $"Документ \"{documentTitle}\" сформирован по данным выбранного обслуживания."
        };
    }

    /// <summary> Возвращает заголовок информационного блока документа сценария. </summary>
    private static string GetScenarioDocumentInfoTitle(string documentKindCode, MaintenanceDocumentTemplate preset)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(documentKindCode) switch
        {
            OrganizationScenarioDocumentKindCodes.WorkOrder => preset.OrderInfoTitle,
            OrganizationScenarioDocumentKindCodes.WorkAct => preset.MaintenanceInfoTitle,
            OrganizationScenarioDocumentKindCodes.InspectionAct => "Данные осмотра",
            OrganizationScenarioDocumentKindCodes.Receipt => "Данные услуги",
            _ => "Данные документа"
        };
    }

    /// <summary> Добавляет блок подписей в документ сценария. </summary>
    private static void AddScenarioSignatures(Body body, WordScenarioMaintenanceDocumentModel model)
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

        var signatureRow = table.AppendChild(new TableRow());
        WordTableComponents.AddTableCell(signatureRow, $"________________ / {signatory}", "4000", JustificationValues.Left, "FFFFFF");
        WordTableComponents.AddTableCell(signatureRow, "________________ / __________________", "4000", JustificationValues.Left, "FFFFFF");

        body.AppendChild(table);
        WordDocumentComponents.AddSpacer(body, 120);
    }

    /// <summary> Определяет, нужно ли отображать подписи для кода документа. </summary>
    private static bool ShouldRenderSignatures(string documentKindCode)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(documentKindCode) is OrganizationScenarioDocumentKindCodes.WorkOrder
            or OrganizationScenarioDocumentKindCodes.WorkAct
            or OrganizationScenarioDocumentKindCodes.InspectionAct
            or OrganizationScenarioDocumentKindCodes.Receipt
            or OrganizationScenarioDocumentKindCodes.ReadinessReport;
    }

    /// <summary> Добавляет раздел со списком пунктов в документ. </summary>
    private static void AddScenarioList(Body body, string title, IEnumerable<string> items)
    {
        WordDocumentComponents.AddSectionHeading(body, title);

        foreach (var item in items)
        {
            body.AppendChild(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "20", After = "80" }),
                new Run(
                new RunProperties(
                    new RunFonts { Ascii = WordDocumentComponents.FontFamily, HighAnsi = WordDocumentComponents.FontFamily, ComplexScript = WordDocumentComponents.FontFamily },
                    new FontSize { Val = "20" }
                ),
                    new Text($"• {item}")
                )
            ));
        }

        WordDocumentComponents.AddSpacer(body, 80);
    }

    /// <summary> Возвращает заголовок блока работ документа сценария. </summary>
    private static string GetScenarioWorksTitle(string documentKindCode, MaintenanceDocumentTemplate preset)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(documentKindCode) switch
        {
            OrganizationScenarioDocumentKindCodes.WorkOrder => preset.PlannedWorksTitle,
            OrganizationScenarioDocumentKindCodes.WorkAct => preset.CompletedWorksTitle,
            OrganizationScenarioDocumentKindCodes.MaterialsSpec => "Работы и материалы",
            OrganizationScenarioDocumentKindCodes.Checklist => "Пункты и работы",
            OrganizationScenarioDocumentKindCodes.Receipt => "Услуги",
            _ => "Работы, услуги и материалы"
        };
    }

    /// <summary> Добавляет специфичные для сценария разделы в документ. </summary>
    private async Task AddScenarioSpecificSections(Body body, WordScenarioMaintenanceDocumentModel model, WordprocessingDocument document)
    {
        var kind = OrganizationScenarioDocumentKindCodes.Normalize(model.DocumentKindCode);

        if (kind == OrganizationScenarioDocumentKindCodes.PhotoReport || kind == OrganizationScenarioDocumentKindCodes.InspectionAct)
        {
            var photos = model.Photos.Where(x => x?.ImageData?.Length > 0).ToList();
            if (photos.Count > 0)
            {
                await WordPhotoComponents.AddMaintenancePhotos(body, photos, document);
            }
            else
            {
                WordDocumentComponents.AddSectionHeading(body, "Фотографии");
                WordDocumentComponents.AddEmptyState(body, "Фотографии пока не добавлены");
            }
        }

        if (kind == OrganizationScenarioDocumentKindCodes.Recommendations)
        {
            AddScenarioList(body, "Рекомендации клиенту", new[]
            {
                "Соблюдать рекомендации мастера по срокам следующего визита.",
                "Проверить состояние выполненных работ после первых дней эксплуатации.",
                "При появлении замечаний обратиться к исполнителю."
            });
        }

        if (kind == OrganizationScenarioDocumentKindCodes.MaterialsSpec)
        {
            AddScenarioList(body, "Материалы и спецификация", new[]
            {
                "Перечень материалов фиксируется в таблице работ и материалов.",
                "Итоговая стоимость отражена в разделе стоимости документа.",
                "Замены материалов согласуются с заказчиком до выполнения работ."
            });
        }

        if (kind == OrganizationScenarioDocumentKindCodes.ReadinessReport)
        {
            AddScenarioList(body, "Готовность техники", new[]
            {
                "Основные работы по записи отражены в документе.",
                "Ответственный подтверждает готовность техники к дальнейшей эксплуатации.",
                "Открытые замечания нужно перенести в отдельную задачу или следующую запись."
            });
        }

        if (kind == OrganizationScenarioDocumentKindCodes.Checklist)
        {
            AddScenarioList(body, "Контрольный список", new[]
            {
                "[ ] Осмотр техники выполнен",
                "[ ] Работы и материалы сверены",
                "[ ] Замечания переданы ответственному",
                "[ ] Документ согласован с заказчиком"
            });
        }
    }

    /// <summary> Добавляет финансовый блок в документ сценария. </summary>
    private static void AddScenarioFinance(Body body, WordScenarioMaintenanceDocumentModel model)
    {
        var kindCode = OrganizationScenarioDocumentKindCodes.Normalize(model.DocumentKindCode) ?? string.Empty;
        var netAmount = Math.Max(0, model.GrossAmount - model.DiscountAmount);

        WordDocumentComponents.AddSectionHeading(body, GetScenarioFinanceTitle(kindCode));

        var table = WordTableComponents.CreateInfoTable();

        if (kindCode == OrganizationScenarioDocumentKindCodes.WorkOrder)
        {
            WordTableComponents.AddInfoRow(table, "Предварительная сумма", WordDocumentComponents.FormatMoney(model.GrossAmount));
            if (model.DiscountAmount > 0)
            {
                WordTableComponents.AddInfoRow(table, "Скидка", WordDocumentComponents.FormatMoney(model.DiscountAmount));
            }
            WordTableComponents.AddInfoRow(table, "Плановая стоимость", WordDocumentComponents.FormatMoney(netAmount));
        }
        else
        {
            WordTableComponents.AddInfoRow(table, "Сумма", WordDocumentComponents.FormatMoney(model.GrossAmount));
            if (model.DiscountAmount > 0)
            {
                WordTableComponents.AddInfoRow(table, "Скидка", WordDocumentComponents.FormatMoney(model.DiscountAmount));
            }
            WordTableComponents.AddInfoRow(table, "Итого к оплате", WordDocumentComponents.FormatMoney(netAmount));
            WordTableComponents.AddInfoRow(table, "Оплачено", WordDocumentComponents.FormatMoney(model.PaidAmount));
            WordTableComponents.AddInfoRow(table, "Остаток", WordDocumentComponents.FormatMoney(model.DebtAmount));
            WordTableComponents.AddInfoRow(table, "Статус оплаты", GetPaymentStatusText(model.PaymentStatus));
        }

        body.AppendChild(table);
        WordDocumentComponents.AddSpacer(body, 160);
    }

    /// <summary> Возвращает заголовок финансового блока по коду документа. </summary>
    private static string GetScenarioFinanceTitle(string documentKindCode)
    {
        return OrganizationScenarioDocumentKindCodes.Normalize(documentKindCode) switch
        {
            OrganizationScenarioDocumentKindCodes.WorkOrder => "Плановая стоимость",
            OrganizationScenarioDocumentKindCodes.Receipt => "Оплата",
            OrganizationScenarioDocumentKindCodes.MaterialsSpec => "Итоги по материалам",
            _ => "Стоимость работ"
        };
    }

}
