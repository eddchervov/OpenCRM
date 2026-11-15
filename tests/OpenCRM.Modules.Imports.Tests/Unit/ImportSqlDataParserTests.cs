using System.Text;

namespace OpenCRM.Modules.Imports.Tests.Unit;

public sealed class ImportSqlDataParserTests
{
    [Fact]
    public void ReadsInsertAndCopyAsDataPreservingQuotesAndEscapes()
    {
        var sql = "-- Выгрузка данных\nINSERT INTO public.clients (id, name) VALUES ('001', 'Анна ''Тест'''), ('002', NULL);\n" +
            "COPY public.cars (id, note) FROM stdin;\n01\tПервая\\nВторая\\tчасть\n02\t\\N\n\\.\n";
        var tables = ImportSqlDataParser.Parse(Encoding.UTF8.GetBytes(sql));

        Assert.Equal(2, tables.Count);
        Assert.Equal("001", tables[0].Rows[0].Values[0]);
        Assert.Equal("Анна 'Тест'", tables[0].Rows[0].Values[1]);
        Assert.Equal(string.Empty, tables[0].Rows[1].Values[1]);
        Assert.Equal("Первая\nВторая\tчасть", tables[1].Rows[0].Values[1]);
        Assert.Equal(string.Empty, tables[1].Rows[1].Values[1]);
    }

    [Theory]
    [InlineData("DROP TABLE clients;")]
    [InlineData("CREATE TABLE clients (id int);")]
    [InlineData("INSERT INTO clients (id) VALUES (now());")]
    [InlineData("INSERT INTO clients VALUES ('1');")]
    [InlineData("COPY clients (id) FROM '/etc/passwd';")]
    [InlineData("COPY clients (id) FROM stdin;\n1\n")]
    [InlineData("INSERT INTO clients (id) VALUES ('1'); DELETE FROM clients;")]
    public void RejectsExecutableAndUnsupportedSyntax(string sql)
    {
        Assert.Throws<InvalidDataException>(() => ImportSqlDataParser.Parse(Encoding.UTF8.GetBytes(sql)));
    }

    [Fact]
    public void LargeCsvModeAcceptsOneHundredThousandRowsWithoutRelaxingLegacyLimits()
    {
        var csv = "Имя;Телефон\n" + string.Join('\n', Enumerable.Range(1, 100000).Select(index => $"Клиент {index};{index:D10}"));
        var bytes = Encoding.UTF8.GetBytes(csv);

        Assert.Equal(100000, Assert.Single(ImportFileParser.Parse(bytes, "large.csv", large: true)).Rows.Count);
        Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(bytes, "large.csv"));
        Assert.Throws<InvalidDataException>(() => ImportFileParser.Parse(Encoding.UTF8.GetBytes(csv + "\nЛишний;000"), "large.csv", large: true));
    }
}
