using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenCRM.Services.Interfaces.Word.Dtos;

namespace OpenCRM.Modules.Documents.Word.Rendering;

/// <summary> Компоненты отрисовки таблиц в Word-документах. </summary>
internal sealed class WordTableComponents
{
    /// <summary> Добавляет таблицу работ или услуг в документ. </summary>
    internal static void AddServicesTable(Body body, List<WordMaintenanceDetail> services, bool showTotal = true, string currencyCode = "RUB")
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            CreateTableBorders(WordDocumentComponents.BorderColor, BorderValues.Single, 6),
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "110", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "110", Type = TableWidthUnitValues.Dxa }
            )
        ));

        table.AppendChild(new TableGrid(
            new GridColumn { Width = "500" },
            new GridColumn { Width = "3400" },
            new GridColumn { Width = "2600" },
            new GridColumn { Width = "1500" }
        ));

        var headerRow = table.AppendChild(new TableRow());
        AddHeaderCell(headerRow, "№", "500", JustificationValues.Center);
        AddHeaderCell(headerRow, "Работа / материал", "3400", JustificationValues.Left);
        AddHeaderCell(headerRow, "Описание", "2600", JustificationValues.Left);
        AddHeaderCell(headerRow, "Стоимость", "1500", JustificationValues.Right);

        decimal total = 0;
        for (var i = 0; i < services.Count; i++)
        {
            var service = services[i];
            var fill = i % 2 == 0 ? "FFFFFF" : WordDocumentComponents.SectionFill;
            var row = table.AppendChild(new TableRow());

            AddTableCell(row, (i + 1).ToString(), "500", JustificationValues.Center, fill);
            AddTableCell(row, service.Quantity > 1 ? $"{service.Name} × {service.Quantity}" : service.Name ?? "", "3400", JustificationValues.Left, fill, bold: true);
            AddTableCell(row, string.IsNullOrWhiteSpace(service.Description) ? "-" : service.Description, "2600", JustificationValues.Left, fill, italic: string.IsNullOrWhiteSpace(service.Description));
            AddTableCell(row, service.Price.HasValue && service.Price > 0 ? WordDocumentComponents.FormatMoney(service.Price.Value, currencyCode) : "-", "1500", JustificationValues.Right, fill, color: service.Price > 0 ? WordDocumentComponents.AccentColor : WordDocumentComponents.MutedColor);

            total += service.Price ?? 0;
        }

        if (showTotal && total > 0)
        {
            var totalRow = table.AppendChild(new TableRow());
            var totalCell = totalRow.AppendChild(new TableCell());
            totalCell.AppendChild(new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "6500" },
                new GridSpan { Val = 3 },
                new Shading { Val = ShadingPatternValues.Clear, Fill = WordDocumentComponents.HeaderFill }
            ));
            totalCell.AppendChild(CreateCellParagraph("Итого", JustificationValues.Right, bold: true));
            AddTableCell(totalRow, WordDocumentComponents.FormatMoney(total, currencyCode), "1500", JustificationValues.Right, WordDocumentComponents.HeaderFill, bold: true, color: WordDocumentComponents.AccentColor);
        }

        body.AppendChild(table);
        WordDocumentComponents.AddSpacer(body, 160);
    }

    /// <summary> Создаёт границы таблицы с заданным оформлением. </summary>
    internal static TableBorders CreateTableBorders(string color, BorderValues borderValue, UInt32Value size)
    {
        return new TableBorders(
            new TopBorder { Val = borderValue, Size = size, Color = color },
            new LeftBorder { Val = borderValue, Size = size, Color = color },
            new BottomBorder { Val = borderValue, Size = size, Color = color },
            new RightBorder { Val = borderValue, Size = size, Color = color },
            new InsideHorizontalBorder { Val = borderValue, Size = size, Color = color },
            new InsideVerticalBorder { Val = borderValue, Size = size, Color = color }
        );
    }

    /// <summary> Создаёт таблицу с информацией. </summary>
    internal static Table CreateInfoTable()
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            CreateTableBorders(WordDocumentComponents.BorderColor, BorderValues.Single, 4),
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "130", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "130", Type = TableWidthUnitValues.Dxa }
            )
        ));

        table.AppendChild(new TableGrid(
            new GridColumn { Width = "2300" },
            new GridColumn { Width = "5700" }
        ));

        return table;
    }

    /// <summary> Добавляет ячейку таблицы с заданным оформлением. </summary>
    internal static void AddTableCell(
        TableRow row,
        string text,
        string width,
        JustificationValues alignment,
        string fill,
        bool bold = false,
        bool italic = false,
        string color = "101828")
    {
        var cell = row.AppendChild(new TableCell());
        cell.AppendChild(new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width },
            new Shading { Val = ShadingPatternValues.Clear, Fill = fill }
        ));
        cell.AppendChild(CreateCellParagraph(text, alignment, bold, italic, color));
    }

    /// <summary> Добавляет ячейку заголовка таблицы. </summary>
    internal static void AddHeaderCell(TableRow row, string text, string width, JustificationValues alignment)
    {
        AddTableCell(row, text, width, alignment, WordDocumentComponents.TableHeaderFill, bold: true, color: "FFFFFF");
    }

    /// <summary> Добавляет строку с подписью и значением в таблицу. </summary>
    internal static void AddInfoRow(Table table, string label, string? value)
    {
        var row = table.AppendChild(new TableRow());
        AddTableCell(row, label, "2300", JustificationValues.Left, WordDocumentComponents.HeaderFill, bold: true, color: WordDocumentComponents.PrimaryColor);
        AddTableCell(row, string.IsNullOrWhiteSpace(value) ? "-" : value, "5700", JustificationValues.Left, "FFFFFF");
    }

    /// <summary> Создаёт абзац для ячейки таблицы с оформлением. </summary>
    internal static Paragraph CreateCellParagraph(
        string text,
        JustificationValues alignment,
        bool bold = false,
        bool italic = false,
        string color = "101828")
    {
        var runProperties = new RunProperties(
            new RunFonts { Ascii = WordDocumentComponents.FontFamily, HighAnsi = WordDocumentComponents.FontFamily, ComplexScript = WordDocumentComponents.FontFamily }
        );

        if (bold) runProperties.AppendChild(new Bold());
        if (italic) runProperties.AppendChild(new Italic());
        runProperties.AppendChild(new Color { Val = color });
        runProperties.AppendChild(new FontSize { Val = "20" });

        return new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = "0" },
                new Justification { Val = alignment }
            ),
            new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve })
        );
    }
}
