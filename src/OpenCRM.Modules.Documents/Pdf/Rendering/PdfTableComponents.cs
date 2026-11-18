using OpenCRM.Services.Interfaces.Pdf.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OpenCRM.Modules.Documents.Pdf.Rendering;

/// <summary> Общие компоненты отрисовки таблиц в PDF-документах. </summary>
internal sealed class PdfTableComponents
{
    /// <summary> Отрисовывает таблицу работ или услуг обслуживания. </summary>
    internal static void RenderServices(IContainer container, List<MaintenanceDetail> services, bool showTotal = true, string currencyCode = "RUB")
    {
        container.Border(1).BorderColor(PdfDocumentComponents.BorderColor).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(32);
                columns.RelativeColumn(3);
                columns.RelativeColumn(2);
                columns.ConstantColumn(92);
            });

            table.Header(header =>
            {
                RenderHeaderCell(header.Cell(), "№", alignCenter: true);
                RenderHeaderCell(header.Cell(), "Работа / материал");
                RenderHeaderCell(header.Cell(), "Описание");
                RenderHeaderCell(header.Cell(), "Стоимость", alignRight: true);
            });

            var rowIndex = 0;
            foreach (var service in services)
            {
                var fill = rowIndex % 2 == 0 ? "#FFFFFF" : PdfDocumentComponents.SoftFillColor;

                RenderBodyCell(table.Cell(), (rowIndex + 1).ToString(), fill, alignCenter: true);
                RenderBodyCell(table.Cell(), service.Quantity > 1 ? $"{service.Name} × {service.Quantity}" : service.Name, fill, bold: true);
                RenderBodyCell(table.Cell(), string.IsNullOrWhiteSpace(service.Description) ? "-" : service.Description, fill, color: string.IsNullOrWhiteSpace(service.Description) ? PdfDocumentComponents.MutedColor : PdfDocumentComponents.TextColor);
                RenderBodyCell(table.Cell(), service.Price.HasValue && service.Price > 0 ? PdfDocumentComponents.FormatMoney(service.Price.Value, currencyCode) : "-", fill, alignRight: true, color: service.Price > 0 ? PdfDocumentComponents.AccentColor : PdfDocumentComponents.MutedColor);

                rowIndex++;
            }

            var total = services.Sum(x => x.Price ?? 0);
            if (showTotal && total > 0)
            {
                table.Footer(footer =>
                {
                    footer.Cell().ColumnSpan(3)
                        .Background(PdfDocumentComponents.PrimaryLightColor)
                        .Padding(8)
                        .AlignRight()
                        .Text("Итого")
                        .FontSize(10)
                        .Bold()
                        .FontColor(PdfDocumentComponents.PrimaryColor);

                    footer.Cell()
                        .Background(PdfDocumentComponents.PrimaryLightColor)
                        .Padding(8)
                        .AlignRight()
                        .Text(PdfDocumentComponents.FormatMoney(total, currencyCode))
                        .FontSize(10)
                        .Bold()
                        .FontColor(PdfDocumentComponents.AccentColor);
                });
            }
        });
    }

    /// <summary> Отрисовывает ячейку тела таблицы с оформлением. </summary>
    internal static void RenderBodyCell(IContainer container, string? text, string fill, bool bold = false, bool alignCenter = false, bool alignRight = false, string color = PdfDocumentComponents.TextColor)
    {
        var cell = container
            .Background(fill)
            .BorderBottom(1)
            .BorderColor(PdfDocumentComponents.BorderColor)
            .PaddingVertical(7)
            .PaddingHorizontal(8);

        if (alignCenter)
            cell = cell.AlignCenter();
        else if (alignRight)
            cell = cell.AlignRight();

        var descriptor = cell.Text(string.IsNullOrWhiteSpace(text) ? "-" : text)
            .FontSize(9.5f)
            .FontColor(color);

        if (bold)
        {
            descriptor.Bold();
        }
    }

    /// <summary> Отрисовывает комментарий в документе. </summary>
    internal static void RenderComment(IContainer container, string comment)
    {
        container.Element(PdfDocumentComponents.Card)
            .Text(comment)
            .FontSize(10)
            .FontColor(PdfDocumentComponents.TextColor)
            .Italic();
    }

    /// <summary> Отрисовывает ячейку заголовка таблицы с оформлением. </summary>
    internal static void RenderHeaderCell(IContainer container, string text, bool alignCenter = false, bool alignRight = false)
    {
        var cell = container
            .Background(PdfDocumentComponents.PrimaryColor)
            .PaddingVertical(7)
            .PaddingHorizontal(8);

        if (alignCenter)
            cell = cell.AlignCenter();
        else if (alignRight)
            cell = cell.AlignRight();

        cell.Text(text)
            .FontSize(9.5f)
            .Bold()
            .FontColor(Colors.White);
    }
}
