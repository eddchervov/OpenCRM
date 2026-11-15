using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Documents.Pdf;
using OpenCRM.Modules.Documents.Word;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Services.Interfaces.Word.Dtos;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;
using PdfMaintenanceDetail = OpenCRM.Services.Interfaces.Pdf.Dtos.MaintenanceDetail;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace OpenCRM.Modules.Documents.Tests.Integration;

public sealed class DocumentRenderingTests
{
    private static readonly byte[] s_onePixelPng = CreateImageData("png");

    public static TheoryData<string, string> DocumentStyles => new()
    {
        { "service-station", "СТО / автосервис" },
        { "detailing", "Детейлинг" },
        { "wrapping", "Студия оклейки" },
        { "car-wash", "Автомойка" },
        { "fleet", "Автопарк" },
        { "driving-school", "Автошкола" },
        { "minimal", "Деловой стиль" }
    };

    public static TheoryData<string, string, string> DocumentKinds => new()
    {
        { OrganizationScenarioDocumentKindCodes.WorkOrder, "Заказчик поручает", "Плановая стоимость" },
        { OrganizationScenarioDocumentKindCodes.WorkAct, "Исполнитель выполнил", "Подписи сторон" },
        { OrganizationScenarioDocumentKindCodes.InspectionAct, "Документ фиксирует состояние техники", "Фотографии" },
        { OrganizationScenarioDocumentKindCodes.PhotoReport, "Документ собирает фотографии проекта", "Фотографии" },
        { OrganizationScenarioDocumentKindCodes.Recommendations, "Рекомендации клиенту", "Рекомендации клиенту" },
        { OrganizationScenarioDocumentKindCodes.MaterialsSpec, "Спецификация материалов и затрат", "Материалы и спецификация" },
        { OrganizationScenarioDocumentKindCodes.Receipt, "Документ подтверждает оказанную услугу", "Оплата" },
        { OrganizationScenarioDocumentKindCodes.ReadinessReport, "Готовность к эксплуатации", "Готовность техники" },
        { OrganizationScenarioDocumentKindCodes.MaintenanceReport, "Документ \"Тестовый документ\" сформирован", "Стоимость работ" },
        { OrganizationScenarioDocumentKindCodes.Checklist, "Контрольный список", "Контрольный список" }
    };

    public static TheoryData<string?, string?, string> DocumentStyleFallbacks => new()
    {
        { "unknown-style", "detailing", "СТО / автосервис" },
        { "service", "ignored", "СТО / автосервис" },
        { "auto-service", "ignored", "СТО / автосервис" },
        { "autoservice", "ignored", "СТО / автосервис" },
        { null, "detailing", "Детейлинг" },
        { null, null, "СТО / автосервис" },
        { "wrap", "ignored", "Студия оклейки" },
        { "wash", "ignored", "Автомойка" },
        { "carwash", "ignored", "Автомойка" },
        { "school", "ignored", "Автошкола" },
        { "driving", "ignored", "Автошкола" },
        { "auto", "wrapping", "Студия оклейки" },
        { "auto", "car-wash", "Автомойка" },
        { "auto", "fleet", "Автопарк" },
        { "auto", "driving-school", "Автошкола" },
        { "auto", "personal", "Деловой стиль" },
        { null, "unknown-scenario", "СТО / автосервис" }
    };

    public static TheoryData<string, bool> PhotoDocumentKinds => new()
    {
        { OrganizationScenarioDocumentKindCodes.PhotoReport, true },
        { OrganizationScenarioDocumentKindCodes.PhotoReport, false },
        { OrganizationScenarioDocumentKindCodes.InspectionAct, true },
        { OrganizationScenarioDocumentKindCodes.InspectionAct, false }
    };

    public static TheoryData<string, string> ImageFormats => new()
    {
        { "png", "image/png" },
        { "jpeg", "image/jpeg" },
        { "gif", "image/gif" },
        { "bmp", "image/bmp" },
        { "webp", "image/webp" }
    };

    public static TheoryData<byte[]> InvalidImageData => new()
    {
        { [0x01, 0x02, 0x03, 0x04, 0x05] },
        { s_onePixelPng[..12] },
        { CreateImageData("tiff") }
    };

    public static TheoryData<string> ActAndOrderKinds => new()
    {
        { "act" },
        { "order" }
    };

    public static TheoryData<string, string> ScenarioPaymentStatuses => new()
    {
        { "partial", "Частично оплачено" },
        { "overpaid", "Переплата" },
        { "unknown", "Не оплачено" }
    };

    public static TheoryData<int, string> MaintenanceTypes => new()
    {
        { (int)MaintenanceType.Repair, "Плановое ТО" },
        { (int)MaintenanceType.Refill, "Заправка топливом" },
        { (int)MaintenanceType.Other, "Другое" },
        { 999, "Обслуживание" }
    };

    public static TheoryData<int, string> WorkCountForms => new()
    {
        { 2, "2 услуги" },
        { 5, "5 услуг" },
        { 11, "11 услуг" },
        { 21, "21 услуга" }
    };

    [Theory]
    [InlineData("Север CRM")]
    [InlineData("Studio & Service")]
    public async Task DocumentsUseConfiguredProjectName(string projectName)
    {
        var brand = new Helpers.ProjectBrand(projectName);
        var pdfBytes = await new QuestPdfGeneratorService(brand)
            .GenerateMaintenanceWorkOrderPdfAsync(CreateWorkActPdfModel());

        using var pdf = PdfDocument.Open(pdfBytes);
        var pdfText = string.Join(" ", pdf.GetPages().Select(page => page.Text));

        Assert.Contains(projectName, pdfText);
        Assert.DoesNotContain(Helpers.ProjectBrand.DefaultName, pdfText);

        var wordBytes = await new DocxGeneratorService(brand)
            .GenerateMaintenanceWorkOrderDocxAsync(CreateWordWorkActModel());

        using var stream = new MemoryStream(wordBytes);
        using var word = WordprocessingDocument.Open(stream, false);
        var wordText = word.MainDocumentPart!.Document.InnerText;

        Assert.Contains(projectName, wordText);
        Assert.DoesNotContain(Helpers.ProjectBrand.DefaultName, wordText);
    }

    [Fact]
    public async Task Vehicle_history_pdf_contains_russian_content_and_embeds_photo()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateVehiclePdfAsync(CreateVehiclePdfModel(), includePhotos: true);

        AssertPdfContent(bytes, 2, "История обслуживания техники", "Toyota Camry", "Новосибирск", "Замена масла");
    }

    [Fact]
    public async Task VehiclePdfKeepsSummaryHeadingWithMetricsOnSamePage()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateVehiclePdfAsync(CreateVehiclePdfModel(), true);
        using var document = PdfDocument.Open(bytes);
        var summaryPage = Assert.Single(document.GetPages(), page => page.Text.Contains("Сводка"));

        Assert.Contains("Общие затраты", summaryPage.Text);
    }

    [Fact]
    public async Task WorkOrderPdfKeepsSignatureHeadingsWithSignatureLinesOnSamePage()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateMaintenanceWorkOrderPdfAsync(CreateWorkActPdfModel("detailing"));
        using var document = PdfDocument.Open(bytes);
        var signaturePage = Assert.Single(document.GetPages(), page => page.Text.Contains("подпись, расшифровка"));

        Assert.Contains("Исполнитель", signaturePage.Text);
        Assert.Contains("Заказчик", signaturePage.Text);
    }

    [Fact]
    public async Task WorkOrderPdfAllowsLongSignatoryNameToContinueAcrossPages()
    {
        var model = CreateWorkActPdfModel("detailing");
        model.ExecutorSignatoryName = string.Join(" ", Enumerable.Repeat("Петров Александр Александрович", 140));
        var bytes = await new QuestPdfGeneratorService().GenerateMaintenanceWorkOrderPdfAsync(model);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "standalone-order-long-preview.pdf"), bytes);
        using var document = PdfDocument.Open(bytes);

        Assert.True(document.NumberOfPages > 1);
        Assert.Contains("Петров", string.Concat(document.GetPages().Select(page => page.Text)));
    }

    [Fact]
    public async Task Maintenance_pdf_contains_work_and_intake_details()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateMaintenancePdfAsync(CreateMaintenancePdfModel(), includePhotos: true);

        AssertPdfContent(bytes, 2, "Обслуживание", "Замена масла", "Приёмка", "Повреждение бампера");
    }

    [Fact]
    public async Task Intake_pdf_contains_vehicle_state_and_before_photos()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateMaintenanceIntakePdfAsync(new MaintenanceIntakePdfModel
        {
            Vehicle = CreatePdfVehicle(),
            MaintenanceReference = "ЗН-2026-001",
            RecordedDate = "09.10.2026 10:30",
            GeneratedDate = "09.10.2026",
            OrganizationName = "Северный сервис",
            ClientName = "Иван Иванов",
            Performer = "Мастер Петров",
            Mileage = 125000,
            Completeness = "Запасное колесо, домкрат",
            VisibleDamage = "Царапина на бампере",
            Notes = "Ключи переданы",
            BeforePhotos = [new PhotoInfo { Caption = "Передний бампер", ImageData = s_onePixelPng }]
        });

        AssertPdfContent(bytes, 1, "АКТ ПРИЁМКИ", "ЗН-2026-001", "Северный сервис", "Царапина на бампере", "Фото 1");
    }

    [Fact]
    public async Task IntakePdfPreservesMarkerPositionsAndNumbersInAllViews()
    {
        const string diagramVersion = "sedan-v1";
        var views = new[] { "front", "rear", "left", "right" };
        var damages = views.Select((view, index) => new MaintenanceIntakeDamagePdfInfo
        {
            Number = index + 1, ViewCode = view,
            ViewName = new[] { "Спереди", "Сзади", "Левый борт", "Правый борт" }[index],
            DamageTypeName = "Царапина", X = 0.55, Y = 0.55, Description = "Место повреждения на схеме"
        }).ToArray();

        var bytes = await new QuestPdfGeneratorService().GenerateMaintenanceIntakePdfAsync(new MaintenanceIntakePdfModel
        {
            Vehicle = CreatePdfVehicle(), MaintenanceReference = "ОСМОТР-001",
            RecordedDate = "23.10.2026 14:00", GeneratedDate = "23.10.2026",
            OrganizationName = "Детейлинг-студия", ClientName = "Иван Иванов",
            VisibleDamage = "Исторические замечания сохранены", Damages = damages,
            DiagramVersion = diagramVersion
        });

        AssertPdfContent(bytes, 4, "ОСМОТР-001", "Спереди", "Левый борт", "Место повреждения на схеме");
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, $"intake-damages-{diagramVersion}-preview.pdf"), bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown-version")]
    public async Task IntakePdfCannotSubstituteTheCurrentDiagramForAnUnavailableVersion(string? diagramVersion)
    {
        var model = new MaintenanceIntakePdfModel
        {
            Vehicle = CreatePdfVehicle(), MaintenanceReference = "ОСМОТР-ВЕРСИЯ",
            RecordedDate = "23.10.2026", GeneratedDate = "23.10.2026",
            DiagramVersion = diagramVersion,
            Damages = [new MaintenanceIntakeDamagePdfInfo
            {
                Number = 1, ViewCode = "rear", ViewName = "Сзади", X = 0.62, Y = 0.45
            }]
        };

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => new QuestPdfGeneratorService().GenerateMaintenanceIntakePdfAsync(model));

        Assert.Contains("версия схемы осмотра недоступна", exception.ToString());
    }

    [Fact]
    public async Task Intake_pdf_marks_missing_optional_vehicle_and_acceptance_fields()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateMaintenanceIntakePdfAsync(new MaintenanceIntakePdfModel
        {
            Vehicle = new VehicleInfo
            {
                BrandName = string.Empty,
                ModelName = string.Empty,
                RegistrationPlate = null,
                YearIssue = null,
                Description = null
            },
            MaintenanceReference = "ЗН-2026-002",
            RecordedDate = string.Empty,
            GeneratedDate = string.Empty,
            OrganizationName = null,
            ClientName = null,
            Performer = null,
            Mileage = null,
            Completeness = null,
            VisibleDamage = null,
            Notes = null
        });

        AssertPdfContent(bytes, 0, "ЗН-2026-002", "Организация", "Не указано", "Государственный номер", "Год выпуска");
    }

    [Fact]
    public async Task Work_act_pdf_contains_executor_customer_and_work_items()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateMaintenanceWorkActPdfAsync(CreateWorkActPdfModel());

        AssertPdfContent(bytes, 0, "Акт выполненных работ", "Дата выполнения", "Иван Иванов", "Северный сервис", "Замена масла", "Исполнитель");
    }

    [Fact]
    public async Task Work_order_pdf_contains_planned_work_and_notice()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateMaintenanceWorkOrderPdfAsync(CreateWorkActPdfModel());

        AssertPdfContent(bytes, 0, "Заказ-наряд", "Плановая дата", "Иван Иванов", "Северный сервис", "Замена масла", "Исполнитель");
    }

    [Fact]
    public async Task Scenario_pdf_contains_document_finance_and_work()
    {
        var bytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(CreateScenarioPdfModel());

        AssertPdfContent(bytes, 0, "Акт оказанных услуг", "Иван Иванов", "Северный сервис", "Замена масла", "Оплачено");
    }

    [Fact]
    public async Task Maintenance_docx_is_valid_and_contains_details_and_images()
    {
        var bytes = await new DocxGeneratorService().GenerateMaintenanceDocxAsync(CreateWordMaintenanceModel(), includePhotos: true);

        AssertDocxContent(bytes, expectedImages: 2, "Отчет об обслуживании техники", "Toyota Camry", "Северный сервис", "Замена масла", "Повреждение бампера");
    }

    [Fact]
    public async Task Vehicle_history_docx_is_valid_and_contains_history_and_images()
    {
        var bytes = await new DocxGeneratorService().GenerateVehicleHistoryDocxAsync(CreateWordVehicleModel(), includePhotos: true);

        AssertDocxContent(bytes, expectedImages: 2, "История обслуживания техники", "Toyota Camry", "Замена масла");
    }

    [Fact]
    public async Task Work_act_docx_is_valid_and_contains_signatures_and_works()
    {
        var bytes = await new DocxGeneratorService().GenerateMaintenanceWorkActDocxAsync(CreateWordWorkActModel());

        AssertDocxContent(bytes, expectedImages: 0, "Акт выполненных работ", "Дата выполнения", "Иван Иванов", "Северный сервис", "Замена масла", "Подписи сторон");
    }

    [Fact]
    public async Task Work_order_docx_is_valid_and_contains_notice_and_works()
    {
        var bytes = await new DocxGeneratorService().GenerateMaintenanceWorkOrderDocxAsync(CreateWordWorkActModel());

        AssertDocxContent(bytes, expectedImages: 0, "Заказ-наряд", "Плановая дата", "Дата заказ-наряда", "Иван Иванов", "Северный сервис", "Замена масла", "Подписи сторон");
    }

    [Fact]
    public async Task Standalone_work_act_and_order_documents_show_discount_and_net_total()
    {
        var pdfGenerator = new QuestPdfGeneratorService();
        var wordGenerator = new DocxGeneratorService();
        var pdfAct = CreateWorkActPdfModel();
        var pdfOrder = CreateWorkActPdfModel();
        var wordAct = CreateWordWorkActModel();
        var wordOrder = CreateWordWorkActModel();
        pdfAct.Works = [new PdfMaintenanceDetail { Name = "Полировка", Quantity = 1, Price = 1500 }];
        pdfOrder.Works = [new PdfMaintenanceDetail { Name = "Полировка", Quantity = 1, Price = 1500 }];
        wordAct.Works = [new WordMaintenanceDetail { Name = "Полировка", Quantity = 1, Price = 1500 }];
        wordOrder.Works = [new WordMaintenanceDetail { Name = "Полировка", Quantity = 1, Price = 1500 }];
        pdfAct.DiscountAmount = pdfOrder.DiscountAmount = wordAct.DiscountAmount = wordOrder.DiscountAmount = 100;

        var pdfActBytes = await pdfGenerator.GenerateMaintenanceWorkActPdfAsync(pdfAct);
        var pdfOrderBytes = await pdfGenerator.GenerateMaintenanceWorkOrderPdfAsync(pdfOrder);
        var wordActBytes = await wordGenerator.GenerateMaintenanceWorkActDocxAsync(wordAct);
        var wordOrderBytes = await wordGenerator.GenerateMaintenanceWorkOrderDocxAsync(wordOrder);

        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "standalone-act-discount-preview.pdf"), pdfActBytes);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "standalone-order-discount-preview.pdf"), pdfOrderBytes);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "standalone-act-discount-preview.docx"), wordActBytes);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "standalone-order-discount-preview.docx"), wordOrderBytes);
        AssertPdfContent(pdfActBytes, 0, "Полировка", "Работы и материалы", "Скидка");
        AssertPdfContent(pdfOrderBytes, 0, "Полировка", "Работы и материалы", "Скидка");
        AssertDocxContent(wordActBytes, 0, "Полировка", "Работы и материалы", "Скидка");
        AssertDocxContent(wordOrderBytes, 0, "Полировка", "Работы и материалы", "Скидка");
        foreach (var text in new[] { GetPdfText(pdfActBytes), GetPdfText(pdfOrderBytes), GetDocxText(wordActBytes), GetDocxText(wordOrderBytes) })
        {
            AssertWorkSummary(text, 1500, 100);
        }
    }

    [Fact]
    public async Task Standalone_work_act_and_order_documents_keep_total_when_there_is_no_discount()
    {
        var pdfGenerator = new QuestPdfGeneratorService();
        var wordGenerator = new DocxGeneratorService();
        var pdfAct = CreateWorkActPdfModel();
        var pdfOrder = CreateWorkActPdfModel();
        var wordAct = CreateWordWorkActModel();
        var wordOrder = CreateWordWorkActModel();
        pdfAct.Works = [new PdfMaintenanceDetail { Name = "Полировка", Quantity = 1, Price = 1500 }];
        pdfOrder.Works = [new PdfMaintenanceDetail { Name = "Полировка", Quantity = 1, Price = 1500 }];
        wordAct.Works = [new WordMaintenanceDetail { Name = "Полировка", Quantity = 1, Price = 1500 }];
        wordOrder.Works = [new WordMaintenanceDetail { Name = "Полировка", Quantity = 1, Price = 1500 }];

        var pdfActBytes = await pdfGenerator.GenerateMaintenanceWorkActPdfAsync(pdfAct);
        var pdfOrderBytes = await pdfGenerator.GenerateMaintenanceWorkOrderPdfAsync(pdfOrder);
        var wordActBytes = await wordGenerator.GenerateMaintenanceWorkActDocxAsync(wordAct);
        var wordOrderBytes = await wordGenerator.GenerateMaintenanceWorkOrderDocxAsync(wordOrder);

        AssertPdfContent(pdfActBytes, 0, "Полировка", "Работы и материалы");
        AssertPdfContent(pdfOrderBytes, 0, "Полировка", "Работы и материалы");
        AssertDocxContent(wordActBytes, 0, "Полировка", "Работы и материалы");
        AssertDocxContent(wordOrderBytes, 0, "Полировка", "Работы и материалы");
        foreach (var text in new[] { GetPdfText(pdfActBytes), GetPdfText(pdfOrderBytes), GetDocxText(wordActBytes), GetDocxText(wordOrderBytes) })
        {
            AssertWorkSummary(text, 1500, 0);
        }
        Assert.DoesNotContain("Скидка", GetPdfText(pdfActBytes), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Скидка", GetPdfText(pdfOrderBytes), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Скидка", GetDocxText(wordActBytes), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Скидка", GetDocxText(wordOrderBytes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scenario_docx_is_valid_and_contains_document_finance_and_work()
    {
        var bytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(CreateWordScenarioModel());

        AssertDocxContent(bytes, expectedImages: 0, "Акт оказанных услуг", "Иван Иванов", "Северный сервис", "Замена масла", "Оплачено");
    }

    [Theory]
    [MemberData(nameof(DocumentStyles))]
    public async Task Act_order_and_scenario_documents_render_for_every_style(string styleCode, string expectedStyleLabel)
    {
        var pdfGenerator = new QuestPdfGeneratorService();
        var wordGenerator = new DocxGeneratorService();
        var workActPdf = await pdfGenerator.GenerateMaintenanceWorkActPdfAsync(CreateWorkActPdfModel(styleCode));
        var workOrderPdf = await pdfGenerator.GenerateMaintenanceWorkOrderPdfAsync(CreateWorkActPdfModel(styleCode));
        var scenarioPdf = await pdfGenerator.GenerateScenarioMaintenanceDocumentPdfAsync(CreateScenarioPdfModel(styleCode));
        var workActWord = await wordGenerator.GenerateMaintenanceWorkActDocxAsync(CreateWordWorkActModel(styleCode));
        var workOrderWord = await wordGenerator.GenerateMaintenanceWorkOrderDocxAsync(CreateWordWorkActModel(styleCode));
        var scenarioWord = await wordGenerator.GenerateScenarioMaintenanceDocumentDocxAsync(CreateWordScenarioModel(styleCode));

        AssertPdfContent(workActPdf, expectedImages: 0, expectedStyleLabel, "Иван Иванов", "Замена масла");
        AssertPdfContent(workOrderPdf, expectedImages: 0, expectedStyleLabel, "Иван Иванов", "Замена масла");
        AssertPdfContent(scenarioPdf, expectedImages: 0, expectedStyleLabel, "Иван Иванов", "Замена масла");
        AssertDocxContent(workActWord, expectedImages: 0, expectedStyleLabel, "Иван Иванов", "Замена масла");
        AssertDocxContent(workOrderWord, expectedImages: 0, expectedStyleLabel, "Иван Иванов", "Замена масла");
        AssertDocxContent(scenarioWord, expectedImages: 0, expectedStyleLabel, "Иван Иванов", "Замена масла");
    }

    [Theory]
    [MemberData(nameof(DocumentKinds))]
    public async Task Every_supported_document_kind_renders_with_empty_optional_data(
        string documentKindCode,
        string expectedPdfSection,
        string expectedWordSection)
    {
        var pdfBytes = await new QuestPdfGeneratorService()
            .GenerateScenarioMaintenanceDocumentPdfAsync(CreateSparseScenarioPdfModel(documentKindCode));
        var wordBytes = await new DocxGeneratorService()
            .GenerateScenarioMaintenanceDocumentDocxAsync(CreateSparseScenarioWordModel(documentKindCode));

        AssertPdfContent(pdfBytes, 0, "Тестовый документ", expectedPdfSection, "Работы, услуги и материалы не указаны");
        AssertDocxContent(wordBytes, expectedImages: 0, "Тестовый документ", expectedWordSection, "Работы, услуги и материалы не указаны");
    }

    [Theory]
    [MemberData(nameof(PhotoDocumentKinds))]
    public async Task Photo_report_and_inspection_act_render_with_and_without_photos(string documentKindCode, bool includePhoto)
    {
        var pdfModel = CreateSparseScenarioPdfModel(documentKindCode);
        var wordModel = CreateSparseScenarioWordModel(documentKindCode);
        if (includePhoto)
        {
            pdfModel.Photos = [new PhotoInfo { Caption = "Фотография проверки", ImageData = s_onePixelPng }];
            wordModel.Photos = [new WordPhotoInfo { ImageData = s_onePixelPng }];
        }

        var pdfBytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(pdfModel);
        var wordBytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(wordModel);
        var expectedImageCount = includePhoto ? 1 : 0;
        var expectedPdfPhotoText = includePhoto
            ? "Фотография проверки"
            : documentKindCode == OrganizationScenarioDocumentKindCodes.PhotoReport
                ? "Фотографии отсутствуют"
                : "Фотографии осмотра не загружены";
        var expectedWordPhotoText = includePhoto ? "Фото 1" : "Фотографии пока не добавлены";

        AssertPdfContent(pdfBytes, expectedImageCount, expectedPdfPhotoText);
        AssertDocxContent(wordBytes, expectedImageCount, expectedWordPhotoText);
    }

    [Theory]
    [MemberData(nameof(DocumentStyleFallbacks))]
    public async Task Scenario_style_falls_back_to_scenario_or_default(string? styleCode, string? scenarioCode, string expectedStyleLabel)
    {
        var pdfModel = CreateScenarioPdfModel(styleCode);
        var wordModel = CreateWordScenarioModel(styleCode);
        pdfModel.ScenarioCode = scenarioCode!;
        wordModel.ScenarioCode = scenarioCode!;

        var pdfBytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(pdfModel);
        var wordBytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 0, expectedStyleLabel, "Акт оказанных услуг");
        AssertDocxContent(wordBytes, expectedImages: 0, expectedStyleLabel, "Акт оказанных услуг");
    }

    [Theory]
    [MemberData(nameof(ImageFormats))]
    public async Task Pdf_and_docx_embed_supported_image_formats(string imageFormat, string expectedContentType)
    {
        var imageData = CreateImageData(imageFormat);
        var pdfModel = CreateSparseScenarioPdfModel(OrganizationScenarioDocumentKindCodes.PhotoReport);
        pdfModel.Photos = [new PhotoInfo { Caption = "Проверка изображения", ImageData = imageData }];
        var wordModel = CreateSparseScenarioWordModel(OrganizationScenarioDocumentKindCodes.PhotoReport);
        wordModel.Photos = [new WordPhotoInfo { ImageData = imageData }];

        var pdfBytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(pdfModel);
        var wordBytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 1, "Фотографии проекта", "Проверка изображения");
        AssertDocxContent(wordBytes, expectedImages: 1, "Фотографии", "Фото 1");
        using var stream = new MemoryStream(wordBytes);
        using var document = WordprocessingDocument.Open(stream, false);
        Assert.Equal(expectedContentType, Assert.Single(document.MainDocumentPart!.ImageParts).ContentType);
    }

    [Theory]
    [InlineData(20, 1)]
    [InlineData(1, 20)]
    public async Task Docx_keeps_extreme_photo_aspect_ratios_inside_page_bounds(int width, int height)
    {
        var model = CreateWordMaintenanceModel();
        model.Vehicle!.PhotoData = [];
        model.Maintenance!.Photos = [new WordPhotoInfo { ImageData = CreateImageData("png", width, height) }];

        var bytes = await new DocxGeneratorService().GenerateMaintenanceDocxAsync(model, includePhotos: true);

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var extent = Assert.Single(document.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>());
        Assert.True(extent.Cx!.Value <= 5_400_000L);
        Assert.True(extent.Cy!.Value <= 3_240_000L);
        Assert.True(extent.Cx.Value > 0);
        Assert.True(extent.Cy.Value > 0);
    }

    [Theory]
    [MemberData(nameof(InvalidImageData))]
    public async Task Docx_uses_fallback_without_embedding_invalid_photo_data(byte[] imageData)
    {
        var model = CreateWordMaintenanceModel();
        model.Vehicle!.PhotoData = [];
        model.Maintenance!.Photos = [new WordPhotoInfo { ImageData = imageData }];

        var bytes = await new DocxGeneratorService().GenerateMaintenanceDocxAsync(model, includePhotos: true);

        AssertDocxContent(bytes, expectedImages: 0, "Фотографию не удалось добавить в документ");
    }

    [Fact]
    public async Task Docx_skips_null_and_empty_maintenance_photos()
    {
        var model = CreateWordMaintenanceModel();
        model.Vehicle!.PhotoData = [];
        model.Maintenance!.Photos = [null!, new WordPhotoInfo { ImageData = [] }];

        var bytes = await new DocxGeneratorService().GenerateMaintenanceDocxAsync(model, includePhotos: true);

        AssertDocxContent(bytes, expectedImages: 0, "Обслуживание");
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        var text = string.Join(" ", document.MainDocumentPart!.Document.Body!.Descendants<WordText>().Select(item => item.Text));
        Assert.DoesNotContain("Фотографии (", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Vehicle_history_renders_when_vehicle_is_missing_and_history_is_empty()
    {
        var pdfModel = CreateVehiclePdfModel();
        pdfModel.Vehicle = null!;
        pdfModel.MaintenanceHistory = [];
        pdfModel.GeneratedDate = string.Empty;
        var wordModel = CreateWordVehicleModel();
        wordModel.Vehicle = null!;
        wordModel.MaintenanceHistory = [];
        wordModel.GeneratedDate = string.Empty;

        var pdfBytes = await new QuestPdfGeneratorService().GenerateVehiclePdfAsync(pdfModel, includePhotos: true);
        var wordBytes = await new DocxGeneratorService().GenerateVehicleHistoryDocxAsync(wordModel, includePhotos: true);

        AssertPdfContent(pdfBytes, 0, "История обслуживания техники", "Данные техники отсутствуют", "История обслуживания отсутствует");
        AssertDocxContent(wordBytes, expectedImages: 0, "История обслуживания техники", "Данные техники отсутствуют", "История обслуживания отсутствует");
    }

    [Fact]
    public async Task Vehicle_history_renders_record_without_price_works_comment_or_photos()
    {
        var pdfMaintenance = CreatePdfMaintenance();
        pdfMaintenance.Price = null;
        pdfMaintenance.MaintenanceDetails = [];
        pdfMaintenance.Comment = string.Empty;
        pdfMaintenance.Photos = [];
        var pdfModel = CreateVehiclePdfModel();
        pdfModel.Vehicle!.PhotoData = [];
        pdfModel.MaintenanceHistory = [pdfMaintenance];
        var wordMaintenance = CreateWordMaintenance();
        wordMaintenance.Price = null;
        wordMaintenance.MaintenanceDetails = [];
        wordMaintenance.Comment = string.Empty;
        wordMaintenance.Photos = [];
        var wordModel = CreateWordVehicleModel();
        wordModel.Vehicle!.PhotoData = [];
        wordModel.MaintenanceHistory = [wordMaintenance];

        var pdfBytes = await new QuestPdfGeneratorService().GenerateVehiclePdfAsync(pdfModel, includePhotos: true);
        var wordBytes = await new DocxGeneratorService().GenerateVehicleHistoryDocxAsync(wordModel, includePhotos: true);

        AssertPdfContent(pdfBytes, 0, "История обслуживания техники", "История обслуживания", "08.10.2026");
        AssertDocxContent(wordBytes, expectedImages: 0, "История обслуживания техники", "История обслуживания", "08.10.2026");
    }

    [Fact]
    public async Task Vehicle_history_hides_photos_when_disabled_and_handles_blank_vehicle_identity()
    {
        var pdfModel = CreateVehiclePdfModel();
        pdfModel.Vehicle = new VehicleInfo
        {
            BrandName = string.Empty,
            ModelName = string.Empty,
            RegistrationPlate = string.Empty,
            YearIssue = null,
            Name = " ",
            PhotoData = s_onePixelPng
        };
        pdfModel.GeneratedDate = null!;
        var wordModel = CreateWordVehicleModel();
        wordModel.Vehicle = new WordVehicleInfo
        {
            BrandName = string.Empty,
            ModelName = string.Empty,
            RegistrationPlate = string.Empty,
            YearIssue = null,
            Name = " ",
            PhotoData = s_onePixelPng
        };
        wordModel.GeneratedDate = null!;

        var pdfBytes = await new QuestPdfGeneratorService().GenerateVehiclePdfAsync(pdfModel, includePhotos: false);
        var wordBytes = await new DocxGeneratorService().GenerateVehicleHistoryDocxAsync(wordModel, includePhotos: false);

        AssertPdfContent(pdfBytes, 0, "История обслуживания техники", "Автомобиль", "Не указан", "Замена масла");
        AssertDocxContent(wordBytes, expectedImages: 0, "История обслуживания техники", "Автомобиль", "Не указан", "Замена масла");
    }

    [Fact]
    public async Task Maintenance_report_renders_empty_optional_fields_without_photos_and_formats_engine_hours()
    {
        var pdfModel = CreateMaintenancePdfModel();
        pdfModel.GeneratedDate = string.Empty;
        pdfModel.Vehicle!.PhotoData = [];
        pdfModel.Vehicle.MileageUnit = VehicleMileageUnit.EngineHours;
        pdfModel.Maintenance = new MaintenanceInfo
        {
            Type = (MaintenanceType)999,
            Date = string.Empty,
            Location = string.Empty,
            Performer = string.Empty,
            Mileage = 1250,
            IntakeNotes = null,
            Comment = string.Empty,
            Price = null,
            MaintenanceDetails = [],
            Photos = []
        };
        var wordModel = CreateWordMaintenanceModel();
        wordModel.GeneratedDate = string.Empty;
        wordModel.Vehicle!.PhotoData = [];
        wordModel.Vehicle.MileageUnit = VehicleMileageUnit.EngineHours;
        wordModel.Maintenance = new WordMaintenanceInfo
        {
            Type = 999,
            Date = string.Empty,
            Location = string.Empty,
            Performer = string.Empty,
            Mileage = 1250,
            Comment = string.Empty,
            Price = null,
            MaintenanceDetails = [],
            Photos = []
        };

        var pdfBytes = await new QuestPdfGeneratorService().GenerateMaintenancePdfAsync(pdfModel, includePhotos: true);
        var wordBytes = await new DocxGeneratorService().GenerateMaintenanceDocxAsync(wordModel, includePhotos: true);

        AssertPdfContent(pdfBytes, 0, "Обслуживание", "Моточасы");
        AssertDocxContent(wordBytes, expectedImages: 0, "Обслуживание", "Моточасы");
    }

    [Theory]
    [MemberData(nameof(MaintenanceTypes))]
    public async Task Maintenance_report_renders_each_maintenance_type(int type, string expectedType)
    {
        var pdfModel = CreateMaintenancePdfModel();
        pdfModel.Vehicle!.PhotoData = [];
        pdfModel.Maintenance!.Type = (MaintenanceType)type;
        pdfModel.Maintenance.Photos = [];
        var wordModel = CreateWordMaintenanceModel();
        wordModel.Vehicle!.PhotoData = [];
        wordModel.Maintenance!.Type = type;
        wordModel.Maintenance.Photos = [];

        var pdfBytes = await new QuestPdfGeneratorService().GenerateMaintenancePdfAsync(pdfModel, includePhotos: false);
        var wordBytes = await new DocxGeneratorService().GenerateMaintenanceDocxAsync(wordModel, includePhotos: false);

        AssertPdfContent(pdfBytes, 0, expectedType);
        AssertDocxContent(wordBytes, expectedImages: 0, expectedType);
    }

    [Fact]
    public async Task Maintenance_report_shows_empty_state_when_maintenance_record_is_missing()
    {
        var pdfModel = CreateMaintenancePdfModel();
        pdfModel.Maintenance = null!;
        var wordModel = CreateWordMaintenanceModel();
        wordModel.Maintenance = null!;

        var pdfBytes = await new QuestPdfGeneratorService().GenerateMaintenancePdfAsync(pdfModel, includePhotos: false);
        var wordBytes = await new DocxGeneratorService().GenerateMaintenanceDocxAsync(wordModel, includePhotos: false);

        AssertPdfContent(pdfBytes, 0, "Данные обслуживания отсутствуют");
        AssertDocxContent(wordBytes, expectedImages: 0, "Данные обслуживания отсутствуют");
    }

    [Fact]
    public async Task Work_act_renders_optional_vehicle_fields_and_executor_signature_fallback()
    {
        var pdfModel = CreateWorkActPdfModel();
        pdfModel.VehicleVin = "JTNB11HK303000001";
        pdfModel.VehicleYear = "2020";
        pdfModel.PlannedPeriod = "10–12 октября";
        pdfModel.ExecutorSignatoryName = string.Empty;
        pdfModel.ExecutorSignatoryBasis = "Доверенность";
        var wordModel = CreateWordWorkActModel();
        wordModel.VehicleVin = pdfModel.VehicleVin;
        wordModel.VehicleYear = pdfModel.VehicleYear;
        wordModel.PlannedPeriod = pdfModel.PlannedPeriod;
        wordModel.ExecutorSignatoryName = string.Empty;
        wordModel.ExecutorSignatoryBasis = pdfModel.ExecutorSignatoryBasis;

        var pdfBytes = await new QuestPdfGeneratorService().GenerateMaintenanceWorkActPdfAsync(pdfModel);
        var wordBytes = await new DocxGeneratorService().GenerateMaintenanceWorkActDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 0, "JTNB11HK303000001", "2020", "10–12 октября", "Северный сервис", "Доверенность");
        AssertDocxContent(wordBytes, expectedImages: 0, "JTNB11HK303000001", "2020", "10–12 октября", "Северный сервис", "Доверенность");
    }

    [Theory]
    [MemberData(nameof(ActAndOrderKinds))]
    public async Task Act_and_order_render_vin_without_plate_or_optional_dates(string documentKind)
    {
        var pdfModel = CreateWorkActPdfModel();
        pdfModel.GeneratedDate = null!;
        pdfModel.ActDate = null!;
        pdfModel.VehicleRegistrationPlate = null;
        pdfModel.VehicleVin = "JTNB11HK303000001";
        pdfModel.VehicleYear = null;
        pdfModel.PlannedPeriod = null;
        pdfModel.ExecutorSignatoryName = null!;
        pdfModel.ExecutorSignatoryBasis = null!;
        var wordModel = CreateWordWorkActModel();
        wordModel.GeneratedDate = null!;
        wordModel.ActDate = null!;
        wordModel.VehicleRegistrationPlate = null;
        wordModel.VehicleVin = pdfModel.VehicleVin;
        wordModel.VehicleYear = null;
        wordModel.PlannedPeriod = null;
        wordModel.ExecutorSignatoryName = null!;
        wordModel.ExecutorSignatoryBasis = null!;
        var pdfGenerator = new QuestPdfGeneratorService();
        var wordGenerator = new DocxGeneratorService();
        var pdfBytes = documentKind == "order"
            ? await pdfGenerator.GenerateMaintenanceWorkOrderPdfAsync(pdfModel)
            : await pdfGenerator.GenerateMaintenanceWorkActPdfAsync(pdfModel);
        var wordBytes = documentKind == "order"
            ? await wordGenerator.GenerateMaintenanceWorkOrderDocxAsync(wordModel)
            : await wordGenerator.GenerateMaintenanceWorkActDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 0, "JTNB11HK303000001", "Северный сервис");
        AssertDocxContent(wordBytes, expectedImages: 0, "JTNB11HK303000001", "Северный сервис");
    }

    [Fact]
    public async Task Work_act_renders_service_rows_with_optional_quantity_description_and_price()
    {
        var pdfModel = CreateWorkActPdfModel();
        pdfModel.Works =
        [
            new PdfMaintenanceDetail { Name = "Диагностика", Quantity = 1, Description = string.Empty, Price = null },
            new PdfMaintenanceDetail { Name = "Фильтр", Quantity = 2, Description = "Масляный", Price = 0 },
            new PdfMaintenanceDetail { Name = "Осмотр", Quantity = 1, Description = "Без наценки", Price = 100 },
            new PdfMaintenanceDetail { Name = null!, Quantity = 1, Description = "Позиция без названия", Price = null }
        ];
        var wordModel = CreateWordWorkActModel();
        wordModel.Works =
        [
            new WordMaintenanceDetail { Name = "Диагностика", Quantity = 1, Description = string.Empty, Price = null },
            new WordMaintenanceDetail { Name = "Фильтр", Quantity = 2, Description = "Масляный", Price = 0 },
            new WordMaintenanceDetail { Name = "Осмотр", Quantity = 1, Description = "Без наценки", Price = 100 },
            new WordMaintenanceDetail { Name = null!, Quantity = 1, Description = "Позиция без названия", Price = null }
        ];

        var pdfBytes = await new QuestPdfGeneratorService().GenerateMaintenanceWorkActPdfAsync(pdfModel);
        var wordBytes = await new DocxGeneratorService().GenerateMaintenanceWorkActDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 0, "Диагностика", "Фильтр × 2", "Масляный", "Осмотр", "Без наценки", "Позиция без названия");
        AssertDocxContent(wordBytes, expectedImages: 0, "Диагностика", "Фильтр × 2", "Масляный", "Осмотр", "Без наценки", "Позиция без названия");
    }

    [Theory]
    [MemberData(nameof(ActAndOrderKinds))]
    public async Task Act_and_order_render_without_works_or_optional_dates_and_include_executor_requisites(string documentKind)
    {
        var pdfModel = CreateWorkActPdfModel(" STO ");
        pdfModel.ActNumber = string.Empty;
        pdfModel.ActDate = string.Empty;
        pdfModel.GeneratedDate = string.Empty;
        pdfModel.ExecutorInn = "5400000000";
        pdfModel.ExecutorKpp = "540001001";
        pdfModel.ExecutorOgrn = "1025400000000";
        pdfModel.ExecutorAddress = "Новосибирск, ул. Ленина, 1";
        pdfModel.ExecutorPhone = "+7 383 000-00-00";
        pdfModel.ExecutorEmail = "service@example.ru";
        pdfModel.CustomerName = string.Empty;
        pdfModel.VehicleTitle = string.Empty;
        pdfModel.MaintenanceDate = string.Empty;
        pdfModel.PlannedPeriod = null;
        pdfModel.MaintenanceType = string.Empty;
        pdfModel.Location = string.Empty;
        pdfModel.Performer = string.Empty;
        pdfModel.Mileage = null;
        pdfModel.Comment = null;
        pdfModel.Works = [];
        var wordModel = CreateWordWorkActModel(" STO ");
        wordModel.ActNumber = string.Empty;
        wordModel.ActDate = string.Empty;
        wordModel.GeneratedDate = string.Empty;
        wordModel.ExecutorInn = pdfModel.ExecutorInn;
        wordModel.ExecutorKpp = pdfModel.ExecutorKpp;
        wordModel.ExecutorOgrn = pdfModel.ExecutorOgrn;
        wordModel.ExecutorAddress = pdfModel.ExecutorAddress;
        wordModel.ExecutorPhone = pdfModel.ExecutorPhone;
        wordModel.ExecutorEmail = pdfModel.ExecutorEmail;
        wordModel.CustomerName = string.Empty;
        wordModel.VehicleTitle = string.Empty;
        wordModel.MaintenanceDate = string.Empty;
        wordModel.PlannedPeriod = null;
        wordModel.MaintenanceType = string.Empty;
        wordModel.Location = string.Empty;
        wordModel.Performer = string.Empty;
        wordModel.Mileage = null;
        wordModel.Comment = null;
        wordModel.Works = [];

        var pdfGenerator = new QuestPdfGeneratorService();
        var wordGenerator = new DocxGeneratorService();
        var pdfBytes = documentKind == "order"
            ? await pdfGenerator.GenerateMaintenanceWorkOrderPdfAsync(pdfModel)
            : await pdfGenerator.GenerateMaintenanceWorkActPdfAsync(pdfModel);
        var wordBytes = documentKind == "order"
            ? await wordGenerator.GenerateMaintenanceWorkOrderDocxAsync(wordModel)
            : await wordGenerator.GenerateMaintenanceWorkActDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 0, "СТО / автосервис", "Работы и материалы не указаны", "5400000000", "service@example.ru");
        AssertDocxContent(wordBytes, expectedImages: 0, "СТО / автосервис", "Работы и материалы не указаны", "5400000000", "service@example.ru");
    }

    [Fact]
    public async Task Scenario_document_normalizes_kind_and_style_aliases_and_keeps_custom_title_without_dates()
    {
        var pdfModel = CreateSparseScenarioPdfModel("AUTO");
        pdfModel.ScenarioCode = " DETAIL ";
        pdfModel.DocumentKindCode = " ACCEPTANCE-ACT ";
        pdfModel.DocumentTitle = "Пользовательский заголовок";
        pdfModel.DocumentNumber = string.Empty;
        pdfModel.DocumentDate = string.Empty;
        pdfModel.GeneratedDate = string.Empty;
        var wordModel = CreateSparseScenarioWordModel("AUTO");
        wordModel.ScenarioCode = pdfModel.ScenarioCode;
        wordModel.DocumentKindCode = pdfModel.DocumentKindCode;
        wordModel.DocumentTitle = pdfModel.DocumentTitle;
        wordModel.DocumentNumber = string.Empty;
        wordModel.DocumentDate = string.Empty;
        wordModel.GeneratedDate = string.Empty;

        var pdfBytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(pdfModel);
        var wordBytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 0, "Детейлинг", "Пользовательский заголовок", "Документ фиксирует состояние техники");
        AssertDocxContent(wordBytes, expectedImages: 0, "Детейлинг", "Пользовательский заголовок", "Документ фиксирует состояние техники");
    }

    [Theory]
    [MemberData(nameof(ScenarioPaymentStatuses))]
    public async Task Receipt_renders_optional_executor_vehicle_and_finance_fields(string paymentStatus, string expectedStatus)
    {
        var pdfModel = CreateScenarioPdfModel("auto");
        pdfModel.DocumentKindCode = OrganizationScenarioDocumentKindCodes.Receipt;
        pdfModel.ScenarioCode = "service-station";
        pdfModel.ExecutorInn = "5400000000";
        pdfModel.ExecutorKpp = "540001001";
        pdfModel.ExecutorOgrn = "1025400000000";
        pdfModel.ExecutorAddress = "Новосибирск, ул. Ленина, 1";
        pdfModel.ExecutorPhone = "+7 383 000-00-00";
        pdfModel.ExecutorEmail = "service@example.ru";
        pdfModel.VehicleRegistrationPlate = "А123АА 54";
        pdfModel.VehicleVin = "JTNB11HK303000001";
        pdfModel.VehicleYear = "2020";
        pdfModel.PlannedPeriod = "10–12 октября";
        pdfModel.Location = "Новосибирск";
        pdfModel.Performer = "Мастер Петров";
        pdfModel.Mileage = 45000;
        pdfModel.MileageLabel = "Пробег";
        pdfModel.MileageUnit = "км";
        pdfModel.GrossAmount = 1000;
        pdfModel.DiscountAmount = 100;
        pdfModel.PaidAmount = 500;
        pdfModel.DebtAmount = 400;
        pdfModel.PaymentStatus = paymentStatus;
        var wordModel = CreateWordScenarioModel("auto");
        wordModel.DocumentKindCode = pdfModel.DocumentKindCode;
        wordModel.ScenarioCode = pdfModel.ScenarioCode;
        wordModel.ExecutorInn = pdfModel.ExecutorInn;
        wordModel.ExecutorKpp = pdfModel.ExecutorKpp;
        wordModel.ExecutorOgrn = pdfModel.ExecutorOgrn;
        wordModel.ExecutorAddress = pdfModel.ExecutorAddress;
        wordModel.ExecutorPhone = pdfModel.ExecutorPhone;
        wordModel.ExecutorEmail = pdfModel.ExecutorEmail;
        wordModel.VehicleRegistrationPlate = pdfModel.VehicleRegistrationPlate;
        wordModel.VehicleVin = pdfModel.VehicleVin;
        wordModel.VehicleYear = pdfModel.VehicleYear;
        wordModel.PlannedPeriod = pdfModel.PlannedPeriod;
        wordModel.Location = pdfModel.Location;
        wordModel.Performer = pdfModel.Performer;
        wordModel.Mileage = pdfModel.Mileage;
        wordModel.MileageLabel = pdfModel.MileageLabel;
        wordModel.MileageUnit = pdfModel.MileageUnit;
        wordModel.GrossAmount = pdfModel.GrossAmount;
        wordModel.DiscountAmount = pdfModel.DiscountAmount;
        wordModel.PaidAmount = pdfModel.PaidAmount;
        wordModel.DebtAmount = pdfModel.DebtAmount;
        wordModel.PaymentStatus = paymentStatus;

        var pdfBytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(pdfModel);
        var wordBytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 0, "5400000000", "Новосибирск", "А123АА 54", "JTNB11HK303000001", "2020", "10–12 октября", "Пробег", "км", "Скидка");
        AssertDocxContent(wordBytes, expectedImages: 0, "5400000000", "Новосибирск", "А123АА 54", "JTNB11HK303000001", "2020", "10–12 октября", "Пробег", "км", "Скидка", expectedStatus);
    }

    [Fact]
    public async Task Work_order_renders_discounted_planned_total()
    {
        var pdfModel = CreateScenarioPdfModel("auto");
        pdfModel.DocumentKindCode = OrganizationScenarioDocumentKindCodes.WorkOrder;
        pdfModel.GrossAmount = 1000;
        pdfModel.DiscountAmount = 100;
        var wordModel = CreateWordScenarioModel("auto");
        wordModel.DocumentKindCode = OrganizationScenarioDocumentKindCodes.WorkOrder;
        wordModel.GrossAmount = 1000;
        wordModel.DiscountAmount = 100;

        var pdfBytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(pdfModel);
        var wordBytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(wordModel);

        AssertPdfContent(pdfBytes, 0, "Предварительная сумма", "Скидка", "Плановая стоимость");
        AssertDocxContent(wordBytes, expectedImages: 0, "Предварительная сумма", "Скидка", "Плановая стоимость");
    }

    [Theory]
    [MemberData(nameof(WorkCountForms))]
    public async Task Vehicle_history_uses_correct_word_form_for_service_count(int workCount, string expectedText)
    {
        var maintenance = CreateWordMaintenance();
        maintenance.MaintenanceDetails = Enumerable.Range(0, workCount)
            .Select(index => new WordMaintenanceDetail { Name = $"Работа {index + 1}", Quantity = 1, Price = 100 })
            .ToList();
        var model = CreateWordVehicleModel();
        model.MaintenanceHistory = [maintenance];

        var bytes = await new DocxGeneratorService().GenerateVehicleHistoryDocxAsync(model);

        AssertDocxContent(bytes, expectedImages: 0, "История обслуживания техники", expectedText);
    }

    [Fact]
    public async Task Recommendations_and_material_spec_render_their_work_dependent_content()
    {
        var recommendationsPdf = CreateSparseScenarioPdfModel(OrganizationScenarioDocumentKindCodes.Recommendations);
        recommendationsPdf.Works = [new PdfMaintenanceDetail { Name = "Замена масла", Quantity = 1, Price = 1200 }];
        recommendationsPdf.Comment = "Осмотр после поездки";
        recommendationsPdf.Mileage = 85000;
        recommendationsPdf.MileageUnit = "км";
        var recommendationsWord = CreateSparseScenarioWordModel(OrganizationScenarioDocumentKindCodes.Recommendations);
        recommendationsWord.Works = [new WordMaintenanceDetail { Name = "Замена масла", Quantity = 1, Price = 1200 }];
        recommendationsWord.Comment = recommendationsPdf.Comment;
        recommendationsWord.Mileage = recommendationsPdf.Mileage;
        recommendationsWord.MileageUnit = recommendationsPdf.MileageUnit;

        var materialPdf = CreateSparseScenarioPdfModel(OrganizationScenarioDocumentKindCodes.MaterialsSpec);
        materialPdf.Works = [new PdfMaintenanceDetail { Name = "Замена масла", Quantity = 1, Price = 1200 }];
        var materialWord = CreateSparseScenarioWordModel(OrganizationScenarioDocumentKindCodes.MaterialsSpec);
        materialWord.Works = [new WordMaintenanceDetail { Name = "Замена масла", Quantity = 1, Price = 1200 }];

        var pdfGenerator = new QuestPdfGeneratorService();
        var wordGenerator = new DocxGeneratorService();
        var recommendationsPdfBytes = await pdfGenerator.GenerateScenarioMaintenanceDocumentPdfAsync(recommendationsPdf);
        var recommendationsWordBytes = await wordGenerator.GenerateScenarioMaintenanceDocumentDocxAsync(recommendationsWord);
        var materialPdfBytes = await pdfGenerator.GenerateScenarioMaintenanceDocumentPdfAsync(materialPdf);
        var materialWordBytes = await wordGenerator.GenerateScenarioMaintenanceDocumentDocxAsync(materialWord);

        AssertPdfContent(recommendationsPdfBytes, 0, "Замена масла", "Проверьте рекомендации мастера", "Основной комментарий мастера", "текущего значения");
        AssertDocxContent(recommendationsWordBytes, expectedImages: 0, "Замена масла", "Рекомендации клиенту");
        AssertPdfContent(materialPdfBytes, 0, "Замена масла", "Сумма материалов и работ по позициям");
        AssertDocxContent(materialWordBytes, expectedImages: 0, "Замена масла", "Материалы и спецификация");
    }

    [Theory]
    [MemberData(nameof(InvalidImageData))]
    public async Task Pdf_photo_report_uses_fallback_when_photo_data_is_corrupt(byte[] imageData)
    {
        var model = CreateSparseScenarioPdfModel(OrganizationScenarioDocumentKindCodes.PhotoReport);
        model.Photos = [new PhotoInfo { Caption = "Повреждённый файл", ImageData = imageData }];

        var bytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(model);

        AssertPdfContent(bytes, 0, "Не удалось отобразить фотографию");
    }

    [Fact]
    public async Task Readiness_report_includes_comment_and_photo_report_uses_caption_fallback()
    {
        var readinessPdf = CreateSparseScenarioPdfModel(OrganizationScenarioDocumentKindCodes.ReadinessReport);
        readinessPdf.Comment = "Проверить тормозную систему";
        var readinessWord = CreateSparseScenarioWordModel(OrganizationScenarioDocumentKindCodes.ReadinessReport);
        readinessWord.Comment = readinessPdf.Comment;
        var photoPdf = CreateSparseScenarioPdfModel(OrganizationScenarioDocumentKindCodes.PhotoReport);
        photoPdf.Photos =
        [
            new PhotoInfo { ImageData = [] },
            new PhotoInfo { Caption = string.Empty, ImageData = s_onePixelPng }
        ];
        var photoWord = CreateSparseScenarioWordModel(OrganizationScenarioDocumentKindCodes.PhotoReport);
        photoWord.Photos =
        [
            new WordPhotoInfo { ImageData = [] },
            new WordPhotoInfo { ImageData = s_onePixelPng }
        ];

        var generatorPdf = new QuestPdfGeneratorService();
        var generatorWord = new DocxGeneratorService();
        var readinessPdfBytes = await generatorPdf.GenerateScenarioMaintenanceDocumentPdfAsync(readinessPdf);
        var readinessWordBytes = await generatorWord.GenerateScenarioMaintenanceDocumentDocxAsync(readinessWord);
        var photoPdfBytes = await generatorPdf.GenerateScenarioMaintenanceDocumentPdfAsync(photoPdf);
        var photoWordBytes = await generatorWord.GenerateScenarioMaintenanceDocumentDocxAsync(photoWord);

        AssertPdfContent(readinessPdfBytes, 0, "Проверить тормозную систему", "Дополнительные замечания указаны");
        AssertDocxContent(readinessWordBytes, expectedImages: 0, "Проверить тормозную систему", "Открытые замечания нужно перенести");
        AssertPdfContent(photoPdfBytes, 1, "Фотографии проекта", "Фото 1");
        AssertDocxContent(photoWordBytes, expectedImages: 1, "Фотографии", "Фото 1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Reports_keep_history_when_vehicle_or_optional_fields_are_missing(int missingData)
    {
        var pdf = CreateMaintenancePdfModel();
        var word = CreateWordMaintenanceModel();
        if (missingData == 0)
        {
            pdf.Vehicle = null!;
            word.Vehicle = null!;
        }
        else
        {
            pdf.Vehicle.YearIssue = null;
            pdf.Vehicle.Name = null!;
            pdf.Vehicle.PhotoData = missingData == 1 ? null : [];
            word.Vehicle.YearIssue = null;
            word.Vehicle.Name = null!;
            word.Vehicle.PhotoData = missingData == 1 ? null : [];
        }

        pdf.Maintenance.MaintenanceDetails = missingData == 1 ? null! : [];
        word.Maintenance.MaintenanceDetails = missingData == 1 ? null! : [];
        pdf.Maintenance.Photos = missingData == 1 ? null! : [];
        word.Maintenance.Photos = missingData == 1 ? null! : [];
        var pdfHistory = CreateVehiclePdfModel();
        pdfHistory.Vehicle = pdf.Vehicle;
        pdfHistory.MaintenanceHistory = [pdf.Maintenance];
        var wordHistory = CreateWordVehicleModel();
        wordHistory.Vehicle = word.Vehicle;
        wordHistory.MaintenanceHistory = [word.Maintenance];
        var pdfGenerator = new QuestPdfGeneratorService();
        var wordGenerator = new DocxGeneratorService();

        var pdfBytes = await pdfGenerator.GenerateMaintenancePdfAsync(pdf, true);
        var wordBytes = await wordGenerator.GenerateMaintenanceDocxAsync(word, true);
        var pdfHistoryBytes = await pdfGenerator.GenerateVehiclePdfAsync(pdfHistory, true);
        var wordHistoryBytes = await wordGenerator.GenerateVehicleHistoryDocxAsync(wordHistory, true);

        AssertPdfContent(pdfBytes, 0, "08.10.2026");
        AssertDocxContent(wordBytes, 0, "08.10.2026");
        AssertPdfContent(pdfHistoryBytes, 0, "08.10.2026");
        AssertDocxContent(wordHistoryBytes, 0, "08.10.2026");
    }

    [Fact]
    public async Task Invalid_optional_photos_are_filtered_without_losing_report_content()
    {
        var pdf = CreateMaintenancePdfModel();
        pdf.Vehicle.PhotoData = null;
        pdf.Maintenance.Photos = [null!, new PhotoInfo { ImageData = null! }, new PhotoInfo { ImageData = [] }];
        var word = CreateWordMaintenanceModel();
        word.Vehicle.PhotoData = null;
        word.Maintenance.Photos = [null!, new WordPhotoInfo { ImageData = null! }, new WordPhotoInfo { ImageData = [] }];
        var pdfBytes = await new QuestPdfGeneratorService().GenerateMaintenancePdfAsync(pdf, true);
        var wordBytes = await new DocxGeneratorService().GenerateMaintenanceDocxAsync(word, true);

        AssertPdfContent(pdfBytes, 0, "Замена масла", "Фотографии отсутствуют");
        AssertDocxContent(wordBytes, 0, "Замена масла");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("photo-report")]
    public async Task Scenario_documents_keep_work_rows_for_unknown_kind_and_photo_report(string kind)
    {
        var pdf = CreateScenarioPdfModel();
        pdf.DocumentKindCode = kind;
        var word = CreateWordScenarioModel();
        word.DocumentKindCode = kind;
        word.ExecutorSignatoryName = "Ответственный Сидоров";
        var pdfBytes = await new QuestPdfGeneratorService().GenerateScenarioMaintenanceDocumentPdfAsync(pdf);
        var wordBytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(word);

        AssertPdfContent(pdfBytes, 0, "Замена масла");
        AssertDocxContent(wordBytes, 0, "Замена масла");
    }

    [Fact]
    public async Task Reports_render_missing_maintenance_and_history_with_photos_enabled()
    {
        var maintenance = CreateWordMaintenanceModel();
        maintenance.Maintenance = null!;
        maintenance.Vehicle.PhotoData = null;
        var history = CreateWordVehicleModel();
        history.MaintenanceHistory = null!;
        history.Vehicle.PhotoData = null;
        var pdfMaintenance = CreateMaintenancePdfModel();
        pdfMaintenance.Maintenance = null!;
        pdfMaintenance.Vehicle.PhotoData = null;
        var pdfHistory = CreateVehiclePdfModel();
        pdfHistory.MaintenanceHistory = null!;
        pdfHistory.Vehicle.PhotoData = null;
        var pdfGenerator = new QuestPdfGeneratorService();
        var pdfMaintenanceBytes = await pdfGenerator.GenerateMaintenancePdfAsync(pdfMaintenance, true);
        var pdfHistoryBytes = await pdfGenerator.GenerateVehiclePdfAsync(pdfHistory, true);

        AssertPdfContent(pdfMaintenanceBytes, 0, "Данные обслуживания отсутствуют");
        AssertPdfContent(pdfHistoryBytes, 0, "История обслуживания техники", "Сводка");
        var generator = new DocxGeneratorService();

        var maintenanceBytes = await generator.GenerateMaintenanceDocxAsync(maintenance, true);
        var historyBytes = await generator.GenerateVehicleHistoryDocxAsync(history, true);

        AssertDocxContent(maintenanceBytes, 0, "Данные обслуживания отсутствуют");
        AssertDocxContent(historyBytes, 0, "История обслуживания отсутствует");
    }

    [Fact]
    public async Task Scenario_photo_report_skips_null_entries_and_null_image_data()
    {
        var model = CreateSparseScenarioWordModel(OrganizationScenarioDocumentKindCodes.PhotoReport);
        model.Photos = [null!, new WordPhotoInfo { ImageData = null! }, new WordPhotoInfo { ImageData = [] }];

        var bytes = await new DocxGeneratorService().GenerateScenarioMaintenanceDocumentDocxAsync(model);

        AssertDocxContent(bytes, 0, "Фотографии пока не добавлены");
    }

    private static void AssertPdfContent(byte[] bytes, int expectedImages, params string[] expectedText)
    {
        Assert.True(bytes.Length > 100, "Генератор вернул пустой PDF.");
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

        using var document = PdfDocument.Open(bytes);
        Assert.NotEmpty(document.GetPages());
        Assert.Equal(expectedImages, document.GetPages().Sum(page => page.NumberOfImages));
        var text = string.Join(" ", document.GetPages().Select(page => page.Text));

        foreach (var value in expectedText)
        {
            Assert.Contains(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertWorkSummary(string text, decimal grossAmount, decimal discountAmount)
    {
        var normalized = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        var grossText = string.Concat($"{grossAmount:N2}".Where(character => !char.IsWhiteSpace(character)));
        var netText = string.Concat($"{Math.Max(0, grossAmount - discountAmount):N2}".Where(character => !char.IsWhiteSpace(character)));
        var occurrences = System.Text.RegularExpressions.Regex.Matches(
            normalized, System.Text.RegularExpressions.Regex.Escape(grossText)).Count;

        Assert.Equal(discountAmount > 0 ? 2 : 3, occurrences);
        Assert.Contains(netText, normalized);
        if (discountAmount > 0)
        {
            var discountText = string.Concat($"{discountAmount:N2}".Where(character => !char.IsWhiteSpace(character)));
            Assert.Contains($"Скидка-{discountText}", normalized);
        }
    }

    private static string GetPdfText(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);

        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    private static string GetDocxText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);

        return document.MainDocumentPart?.Document.Body?.InnerText ?? string.Empty;
    }

    private static void AssertDocxContent(byte[] bytes, int expectedImages, params string[] expectedText)
    {
        Assert.True(bytes.Length > 100, "Генератор вернул пустой DOCX.");
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        Assert.NotNull(document.MainDocumentPart?.Document?.Body);

        var validationErrors = new OpenXmlValidator().Validate(document).ToArray();
        Assert.True(
            validationErrors.Length == 0,
            string.Join(Environment.NewLine, validationErrors.Select(error => $"{error.Id}: {error.Description} {error.Path?.XPath}")));
        Assert.Equal(expectedImages, document.MainDocumentPart!.ImageParts.Count());

        var text = string.Join(" ", document.MainDocumentPart.Document.Body!.Descendants<WordText>().Select(item => item.Text));
        foreach (var value in expectedText)
        {
            Assert.Contains(value, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static VehiclePdfModel CreateVehiclePdfModel() => new()
    {
        Vehicle = CreatePdfVehicle(),
        GeneratedDate = "09.10.2026",
        TotalMaintenanceCount = 1,
        TotalSpent = 12500,
        MaintenanceHistory = [CreatePdfMaintenance()]
    };

    private static MaintenancePdfModel CreateMaintenancePdfModel() => new()
    {
        Vehicle = CreatePdfVehicle(),
        Maintenance = CreatePdfMaintenance(),
        GeneratedDate = "09.10.2026",
        CompanyInfo = "Северный сервис"
    };

    private static VehicleInfo CreatePdfVehicle() => new()
    {
        BrandName = "Toyota",
        ModelName = "Camry",
        RegistrationPlate = "А123АА 54",
        YearIssue = 2020,
        Name = "Семейный автомобиль",
        Description = "Седан для ежедневных поездок",
        PhotoData = s_onePixelPng
    };

    private static MaintenanceInfo CreatePdfMaintenance() => new()
    {
        Type = MaintenanceType.Maintenance,
        Date = "08.10.2026",
        Location = "Новосибирск",
        Performer = "Мастер Петров",
        Mileage = 125000,
        IntakeNotes = "Повреждение бампера",
        Comment = "Проверить повторно через месяц",
        Price = 12500,
        MaintenanceDetails = [new PdfMaintenanceDetail { Name = "Замена масла", Description = "Масло 5W-30", Quantity = 4, Price = 3000 }],
        Photos = [new PhotoInfo { Caption = "После обслуживания", ImageData = s_onePixelPng }]
    };

    private static MaintenanceWorkActPdfModel CreateWorkActPdfModel(string styleCode = "service-station") => new()
    {
        DocumentStyleCode = styleCode,
        ActNumber = "АВР-2026-001",
        ActDate = "09.10.2026",
        GeneratedDate = "09.10.2026",
        ExecutorName = "Северный сервис",
        ExecutorInn = "5400000000",
        CustomerName = "Иван Иванов",
        VehicleTitle = "Toyota Camry",
        VehicleRegistrationPlate = "А123АА 54",
        VehicleVin = "JTNB11HK303000001",
        MaintenanceDate = "08.10.2026",
        MaintenanceType = "Плановое ТО",
        Location = "Новосибирск",
        Performer = "Мастер Петров",
        Mileage = 125000,
        MileageLabel = "Пробег",
        MileageUnit = "км",
        Comment = "Работы выполнены в полном объёме",
        Works = [new PdfMaintenanceDetail { Name = "Замена масла", Description = "Масло 5W-30", Quantity = 4, Price = 3000 }]
    };

    private static ScenarioMaintenanceDocumentPdfModel CreateScenarioPdfModel(string? styleCode = "service-station") => new()
    {
        DocumentStyleCode = styleCode!,
        ScenarioCode = styleCode ?? string.Empty,
        DocumentKindCode = OrganizationScenarioDocumentKindCodes.WorkAct,
        DocumentTitle = "Акт оказанных услуг",
        DocumentNumber = "СЦ-2026-001",
        DocumentDate = "09.10.2026",
        GeneratedDate = "09.10.2026",
        ExecutorName = "Северный сервис",
        CustomerName = "Иван Иванов",
        VehicleTitle = "Toyota Camry",
        MaintenanceDate = "08.10.2026",
        MaintenanceType = "Плановое ТО",
        GrossAmount = 12500,
        PaidAmount = 12500,
        PaymentStatus = "paid",
        Works = [new PdfMaintenanceDetail { Name = "Замена масла", Quantity = 1, Price = 12500 }]
    };

    private static WordMaintenanceModel CreateWordMaintenanceModel() => new()
    {
        Vehicle = CreateWordVehicle(),
        GeneratedDate = "09.10.2026",
        CompanyInfo = "Северный сервис",
        Maintenance = CreateWordMaintenance()
    };

    private static WordVehicleModel CreateWordVehicleModel() => new()
    {
        Vehicle = CreateWordVehicle(),
        GeneratedDate = "09.10.2026",
        TotalMaintenanceCount = 1,
        TotalSpent = 12500,
        MaintenanceHistory = [CreateWordMaintenance()]
    };

    private static WordVehicleInfo CreateWordVehicle() => new()
    {
        BrandName = "Toyota",
        ModelName = "Camry",
        RegistrationPlate = "А123АА 54",
        YearIssue = 2020,
        Name = "Семейный автомобиль",
        Description = "Седан для ежедневных поездок",
        PhotoData = s_onePixelPng
    };

    private static WordMaintenanceInfo CreateWordMaintenance() => new()
    {
        Type = 1,
        Date = "08.10.2026",
        Location = "Новосибирск",
        Performer = "Мастер Петров",
        Mileage = 125000,
        Comment = "Повреждение бампера",
        Price = 12500,
        MaintenanceDetails = [new WordMaintenanceDetail { Name = "Замена масла", Description = "Масло 5W-30", Quantity = 4, Price = 3000 }],
        Photos = [new WordPhotoInfo { ImageData = s_onePixelPng }]
    };

    private static WordMaintenanceWorkActModel CreateWordWorkActModel(string styleCode = "service-station") => new()
    {
        DocumentStyleCode = styleCode,
        ActNumber = "АВР-2026-001",
        ActDate = "09.10.2026",
        GeneratedDate = "09.10.2026",
        ExecutorName = "Северный сервис",
        ExecutorSignatoryName = "Мастер Петров",
        CustomerName = "Иван Иванов",
        VehicleTitle = "Toyota Camry",
        VehicleRegistrationPlate = "А123АА 54",
        MaintenanceDate = "08.10.2026",
        MaintenanceType = "Плановое ТО",
        Location = "Новосибирск",
        Performer = "Мастер Петров",
        Mileage = 125000,
        MileageLabel = "Пробег",
        MileageUnit = "км",
        Comment = "Работы выполнены в полном объёме",
        Works = [new WordMaintenanceDetail { Name = "Замена масла", Description = "Масло 5W-30", Quantity = 4, Price = 3000 }]
    };

    private static WordScenarioMaintenanceDocumentModel CreateWordScenarioModel(string? styleCode = "service-station") => new()
    {
        DocumentStyleCode = styleCode!,
        ScenarioCode = styleCode ?? string.Empty,
        DocumentKindCode = OrganizationScenarioDocumentKindCodes.WorkAct,
        DocumentTitle = "Акт оказанных услуг",
        DocumentNumber = "СЦ-2026-001",
        DocumentDate = "09.10.2026",
        GeneratedDate = "09.10.2026",
        ExecutorName = "Северный сервис",
        CustomerName = "Иван Иванов",
        VehicleTitle = "Toyota Camry",
        MaintenanceDate = "08.10.2026",
        MaintenanceType = "Плановое ТО",
        GrossAmount = 12500,
        PaidAmount = 12500,
        PaymentStatus = "paid",
        Works = [new WordMaintenanceDetail { Name = "Замена масла", Quantity = 1, Price = 12500 }]
    };

    private static ScenarioMaintenanceDocumentPdfModel CreateSparseScenarioPdfModel(string documentKindCode) => new()
    {
        DocumentStyleCode = null!,
        ScenarioCode = "service-station",
        DocumentKindCode = documentKindCode,
        DocumentTitle = "Тестовый документ",
        ExecutorName = "Сервис",
        VehicleTitle = "Техника",
        Works = []
    };

    private static WordScenarioMaintenanceDocumentModel CreateSparseScenarioWordModel(string documentKindCode) => new()
    {
        DocumentStyleCode = null!,
        ScenarioCode = "service-station",
        DocumentKindCode = documentKindCode,
        DocumentTitle = "Тестовый документ",
        ExecutorName = "Сервис",
        VehicleTitle = "Техника",
        Works = []
    };

    private static byte[] CreateImageData(string format, int width = 1, int height = 1)
    {
        var pixels = Enumerable.Repeat(new Rgba32(180, 40, 80, 255), width * height).ToArray();
        using var image = Image.LoadPixelData<Rgba32>(pixels, width, height);
        using var stream = new MemoryStream();

        switch (format)
        {
            case "png":
                image.Save(stream, new PngEncoder());
                break;
            case "jpeg":
                image.Save(stream, new JpegEncoder());
                break;
            case "gif":
                image.Save(stream, new GifEncoder());
                break;
            case "bmp":
                image.Save(stream, new BmpEncoder());
                break;
            case "webp":
                image.Save(stream, new WebpEncoder());
                break;
            case "tiff":
                image.Save(stream, new TiffEncoder());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Неподдерживаемый формат изображения.");
        }

        return stream.ToArray();
    }
}
