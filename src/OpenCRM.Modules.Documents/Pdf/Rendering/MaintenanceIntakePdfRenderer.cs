using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Рендерер PDF-документа приёма обслуживания. </summary>
internal sealed class MaintenanceIntakePdfRenderer
{
    /// <summary> Отрисовывает поле приёма с подписью и значением. </summary>
    private static void RenderIntakeField(IContainer container, string label, string? value)
    {
        container.Row(row =>
        {
            row.ConstantItem(145).Text(label).FontSize(9);
            row.RelativeItem().BorderBottom(0.5f).BorderColor(Colors.Grey.Medium)
                .Text(string.IsNullOrWhiteSpace(value) ? "Не указано" : value.Trim())
                .FontSize(10);
        });
    }

    /// <summary> Генерирует PDF-документ приёма обслуживания. </summary>
    internal async Task<byte[]> GenerateMaintenanceIntakePdfAsync(MaintenanceIntakePdfModel model)
    {
        return await Task.Run(() =>
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    PdfDocumentComponents.ConfigurePage(page);
                    page.DefaultTextStyle(TextStyle.Default.FontFamily(PdfDocumentComponents.FontFamily).FontSize(10).FontColor(Colors.Black));

                    page.Content().Column(column =>
                    {
                        column.Spacing(13);
                        column.Item().AlignCenter().Text("АКТ ПРИЁМКИ ТРАНСПОРТНОГО СРЕДСТВА")
                            .FontSize(15).Bold();
                        column.Item().AlignCenter().Text($"к обслуживанию № {model.MaintenanceReference}")
                            .FontSize(9);
                        column.Item().PaddingTop(5).BorderBottom(1).BorderColor(Colors.Black);

                        column.Item().Element(c => RenderIntakeField(c, "Организация", model.OrganizationName));
                        column.Item().Element(c => RenderIntakeField(c, "Клиент", model.ClientName));
                        column.Item().Element(c => RenderIntakeField(c, "Дата и время приёмки", model.RecordedDate));
                        column.Item().Element(c => RenderIntakeField(c, "Принял", model.Performer));

                        column.Item().PaddingTop(7).Text("1. Сведения о транспортном средстве").Bold();
                        column.Item().Element(c => RenderIntakeField(c, "Автомобиль", PdfDocumentComponents.GetVehicleTitle(model.Vehicle)));
                        column.Item().Element(c => RenderIntakeField(c, "Государственный номер", model.Vehicle.RegistrationPlate));
                        column.Item().Element(c => RenderIntakeField(c, "Год выпуска", model.Vehicle.YearIssue?.ToString()));
                        if (!string.IsNullOrWhiteSpace(model.Vehicle.Description))
                        {
                            column.Item().Element(c => RenderIntakeField(c, "Описание", model.Vehicle.Description));
                        }

                        column.Item().PaddingTop(7).Text("2. Состояние при приёмке").Bold();
                        column.Item().Element(c => RenderIntakeField(c,
                            PdfDocumentComponents.GetMileageLabel(model.Vehicle.MileageUnit).TrimEnd(':'),
                            model.Mileage.HasValue
                                ? $"{model.Mileage.Value:n0} {PdfDocumentComponents.GetMileageUnitText(model.Vehicle.MileageUnit)}"
                                : null));
                        column.Item().Element(c => RenderIntakeField(c, "Комплектность", model.Completeness));
                        column.Item().Element(c => RenderIntakeField(c, "Видимые повреждения", model.VisibleDamage));
                        var views = model.Damages.GroupBy(damage => damage.ViewCode).ToArray();
                        foreach (var pair in views.Chunk(2))
                        {
                            column.Item().Row(row =>
                            {
                                row.Spacing(10);
                                foreach (var view in pair)
                                {
                                    row.RelativeItem().Column(diagram =>
                                    {
                                        diagram.Item().Text(view.First().ViewName).FontSize(9);
                                        diagram.Item().Element(container =>
                                            MaintenanceIntakeDamageDiagram.Render(container, model.DiagramVersion, view.Key, view));
                                    });
                                }
                            });
                        }

                        foreach (var damage in model.Damages)
                        {
                            column.Item().Text($"№ {damage.Number}. {damage.ViewName}: {damage.DamageTypeName}. {damage.Description}");
                        }

                        column.Item().Element(c => RenderIntakeField(c, "Дополнительные сведения", model.Notes));

                        if (model.BeforePhotos.Count > 0)
                        {
                            column.Item().PaddingTop(7).Text("3. Фотографии состояния при приёмке").Bold();
                            for (var index = 0; index < model.BeforePhotos.Count; index++)
                            {
                                var photo = model.BeforePhotos[index];
                                var photoNumber = index + 1;
                                column.Item().Border(1).BorderColor(Colors.Grey.Medium).Padding(8).Column(photoColumn =>
                                {
                                    photoColumn.Spacing(6);
                                    photoColumn.Item().Text($"Фото {photoNumber}. {photo.Caption}").FontSize(9);
                                    PdfPhotoComponents.RenderImage(photoColumn.Item(), photo.ImageData ?? [], 500, 300);
                                });
                            }
                        }

                        column.Item().PaddingTop(20).Text("Сведения о состоянии автомобиля и комплектности внесены со слов и при осмотре сторон.")
                            .FontSize(9);
                        column.Item().PaddingTop(26).Row(row =>
                        {
                            row.RelativeItem().Column(signature =>
                            {
                                signature.Item().Text("Передал: ________________________").FontSize(9);
                                signature.Item().Text("подпись / расшифровка").FontSize(8);
                            });
                            row.ConstantItem(20);
                            row.RelativeItem().Column(signature =>
                            {
                                signature.Item().Text("Принял: ________________________").FontSize(9);
                                signature.Item().Text("подпись / расшифровка").FontSize(8);
                            });
                        });
                    });

                    page.Footer().PaddingTop(8).BorderTop(1).BorderColor(Colors.Black).Row(row =>
                    {
                        row.RelativeItem().Text($"Сформировано {model.GeneratedDate}").FontSize(8);
                        row.AutoItem().Text(text =>
                        {
                            text.DefaultTextStyle(TextStyle.Default.FontFamily(PdfDocumentComponents.FontFamily).FontSize(8).FontColor(Colors.Black));
                            text.Span("Страница ");
                            text.CurrentPageNumber();
                        });
                    });
                });
            });

            return document.GeneratePdf();
        });
    }
}
