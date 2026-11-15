using OpenCRM.Modules.Imports;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports.Tests.Unit;

public sealed class ImportMappingTransformationTests
{
    [Theory]
    [InlineData("+7 (912) 345-67-89", "79123456789")]
    [InlineData("8 912 345 67 89", "79123456789")]
    [InlineData("8 912 345 67 8", "8912345678")]
    public void BuildNormalizesRussianPhone(string sourceValue, string expected)
    {
        var (table, mapping) = Create("clientPhone", "client", sourceValue, new ImportFieldTransformation { Type = "phone" });

        var candidate = Assert.Single(ImportMappingBuilder.Build([table], [mapping]));

        Assert.Equal(expected, candidate.Phone);
    }

    [Fact]
    public void BuildConvertsExactDateFormatToIsoDate()
    {
        var (table, mapping) = Create("occurredAt", "order", "15.04.2025", new ImportFieldTransformation
        {
            Type = "date-exact",
            Format = "dd.MM.yyyy"
        });

        var candidate = Assert.Single(ImportMappingBuilder.Build([table], [mapping]));

        Assert.Equal("2025-04-15", candidate.OccurredAt);
    }

    [Fact]
    public void BuildConvertsExactDateTimeFormatToIsoDateTime()
    {
        var (table, mapping) = Create("occurredAt", "order", "2025-04-15T13:20:00", new ImportFieldTransformation
        {
            Type = "date-exact",
            Format = "yyyy-MM-ddTHH:mm:ss"
        });

        var candidate = Assert.Single(ImportMappingBuilder.Build([table], [mapping]));

        Assert.Equal("2025-04-15T13:20:00", candidate.OccurredAt);
    }

    [Fact]
    public void BuildNormalizesDecimalSeparatorsAndWhitespace()
    {
        var (table, mapping) = Create("amount", "payment", "1 234,50", new ImportFieldTransformation { Type = "decimal" });

        var candidate = Assert.Single(ImportMappingBuilder.Build([table], [mapping]));

        Assert.Equal("1234.50", candidate.Amount);
    }

    [Fact]
    public void BuildMapsExplicitValues()
    {
        var (table, mapping) = Create("paymentMethod", "payment", "Наличные", new ImportFieldTransformation
        {
            Type = "value-map",
            Values = new Dictionary<string, string> { ["Наличные"] = "cash" }
        });

        var candidate = Assert.Single(ImportMappingBuilder.Build([table], [mapping]));

        Assert.Equal("cash", candidate.PaymentMethod);
    }

    [Fact]
    public void BuildReportsTableRowAndFieldWhenValueIsNotMapped()
    {
        var (table, mapping) = Create("paymentMethod", "payment", "Терминал", new ImportFieldTransformation
        {
            Type = "value-map",
            Values = new Dictionary<string, string> { ["Наличные"] = "cash" }
        });

        var exception = Assert.Throws<InvalidDataException>(() => ImportMappingBuilder.Build([table], [mapping]));

        Assert.Contains("Таблица «Платежи»", exception.Message);
        Assert.Contains("строка 7", exception.Message);
        Assert.Contains("paymentMethod", exception.Message);
    }

    [Fact]
    public void BuildRejectsDateFormatOutsideAllowList()
    {
        var (table, mapping) = Create("occurredAt", "order", "15.04.2025", new ImportFieldTransformation
        {
            Type = "date-exact",
            Format = "dddd, dd MMMM yyyy"
        });

        Assert.Throws<InvalidDataException>(() => ImportMappingBuilder.Build([table], [mapping]));
    }

    [Fact]
    public void BuildRejectsExplicitNullTransformation()
    {
        var (table, mapping) = Create("clientPhone", "client", "9123456789", new ImportFieldTransformation { Type = "phone" });
        mapping.Transformations["clientPhone"] = null!;

        Assert.Throws<InvalidDataException>(() => ImportMappingBuilder.Build([table], [mapping]));
    }

    [Fact]
    public void BuildRejectsExplicitNullValueMap()
    {
        var (table, mapping) = Create("paymentMethod", "payment", "Наличные", new ImportFieldTransformation
        {
            Type = "value-map",
            Values = null!
        });

        Assert.Throws<InvalidDataException>(() => ImportMappingBuilder.Build([table], [mapping]));
    }

    [Fact]
    public void BuildRejectsValueMapOverLimit()
    {
        var values = Enumerable.Range(0, 101).ToDictionary(index => $"source-{index}", index => $"target-{index}");
        var (table, mapping) = Create("paymentMethod", "payment", "source-1", new ImportFieldTransformation
        {
            Type = "value-map",
            Values = values
        });

        Assert.Throws<InvalidDataException>(() => ImportMappingBuilder.Build([table], [mapping]));
    }

    private static (ImportSourceTable Table, ImportTableMapping Mapping) Create(
        string field,
        string kind,
        string value,
        ImportFieldTransformation transformation)
    {
        var table = new ImportSourceTable
        {
            Id = "source",
            Name = "Платежи",
            Columns = ["Значение"],
            Rows = [new ImportSourceRow { Number = 7, Values = [value] }]
        };
        var mapping = new ImportTableMapping
        {
            TableId = table.Id,
            Kind = kind,
            Fields = new Dictionary<string, int[]> { [field] = [0] },
            Transformations = new Dictionary<string, ImportFieldTransformation> { [field] = transformation }
        };

        return (table, mapping);
    }
}
