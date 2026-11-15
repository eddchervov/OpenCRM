using System.Text;
using System.Text.RegularExpressions;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports;

/// <summary> Читает ограниченную текстовую выгрузку SQL как данные, никогда не исполняя команды. </summary>
public static partial class ImportSqlDataParser
{
    private const int MaxRows = 100_000;

    /// <summary> Принимает INSERT с явными столбцами и PostgreSQL COPY FROM stdin. </summary>
    public static List<ImportSourceTable> Parse(byte[] content)
    {
        if (content.Length is 0 or > 50 * 1024 * 1024)
        {
            throw new InvalidDataException("SQL-выгрузка должна быть непустой и не больше 50 МБ.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("SQL-выгрузку нужно сохранить в UTF-8.", exception);
        }
        if (text.Contains('\0'))
        {
            throw new InvalidDataException("Нулевые символы в SQL-выгрузке не поддерживаются.");
        }

        var tables = new Dictionary<string, ImportSourceTable>(StringComparer.Ordinal);
        var cursor = new Cursor(text);
        var count = 0;
        while (!cursor.End)
        {
            var command = cursor.Word();
            if (command.Equals("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                cursor.ExpectWord("INTO");
                var table = ReadTable(cursor, tables);
                cursor.ExpectWord("VALUES");
                do
                {
                    cursor.Expect('(');
                    var values = new List<string>();
                    do
                    {
                        values.Add(cursor.Value());
                    } while (cursor.Take(','));
                    cursor.Expect(')');
                    AddRow(table, values, ref count);
                } while (cursor.Take(','));
                cursor.Expect(';');
                continue;
            }

            if (command.Equals("COPY", StringComparison.OrdinalIgnoreCase))
            {
                var table = ReadTable(cursor, tables);
                cursor.ExpectWord("FROM");
                cursor.ExpectWord("stdin");
                cursor.Expect(';');
                foreach (var line in cursor.CopyLines())
                {
                    var values = line.Split('\t').Select(DecodeCopyValue).ToList();
                    AddRow(table, values, ref count);
                }
                continue;
            }

            throw new InvalidDataException($"Команда {command} не поддерживается. Нужна выгрузка только данных: INSERT с именами столбцов или COPY FROM stdin. DDL, функции и исполняемый SQL не импортируются.");
        }

        if (count == 0)
        {
            throw new InvalidDataException("В SQL-выгрузке не найдены строки данных.");
        }

        return tables.Values.ToList();
    }

    private static ImportSourceTable ReadTable(Cursor cursor, Dictionary<string, ImportSourceTable> tables)
    {
        var name = cursor.Identifier();
        if (cursor.Take('.'))
        {
            name += "." + cursor.Identifier();
        }

        cursor.Expect('(');
        var columns = new List<string>();
        do
        {
            columns.Add(cursor.Identifier());
        } while (cursor.Take(','));
        cursor.Expect(')');
        if (columns.Count is 0 or > 100 || columns.Distinct(StringComparer.Ordinal).Count() != columns.Count)
        {
            throw new InvalidDataException("Таблица SQL должна содержать до 100 различных столбцов.");
        }

        if (tables.TryGetValue(name, out var table))
        {
            if (!table.Columns.SequenceEqual(columns))
            {
                throw new InvalidDataException($"Состав столбцов таблицы {name} меняется внутри выгрузки.");
            }

            return table;
        }
        if (tables.Count >= 100)
        {
            throw new InvalidDataException("В одной выгрузке поддерживается до 100 таблиц.");
        }

        table = new ImportSourceTable { Id = $"table{tables.Count + 1}", Name = name, Columns = columns };
        tables.Add(name, table);

        return table;
    }

    private static void AddRow(ImportSourceTable table, List<string> values, ref int count)
    {
        if (++count > MaxRows)
        {
            throw new InvalidDataException("SQL-выгрузка содержит более 100000 строк.");
        }

        if (values.Count != table.Columns.Count || values.Any(value => value.Length > 10000 || value.Contains('\0')))
        {
            throw new InvalidDataException("Проверьте число столбцов и длину ячеек SQL (до 10000 символов).");
        }

        table.Rows.Add(new ImportSourceRow { Number = table.Rows.Count + 1, Values = values });
    }

    private static string DecodeCopyValue(string value)
    {
        if (value == "\\N")
        {
            return string.Empty;
        }

        var result = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\')
            {
                result.Append(value[index]);
                continue;
            }
            if (++index == value.Length)
            {
                throw new InvalidDataException("Повреждена escape-последовательность COPY.");
            }

            result.Append(value[index] switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                'b' => '\b',
                'f' => '\f',
                'v' => '\v',
                '\\' => '\\',
                _ => throw new InvalidDataException("Поддерживаются текстовые COPY escape-последовательности; сохраните сложную выгрузку в CSV.")
            });
        }

        return result.ToString();
    }

    private sealed class Cursor(string text)
    {
        private int _position;

        public bool End
        {
            get
            {
                this.Skip();
                return this._position == text.Length;
            }
        }

        public bool Take(char symbol)
        {
            this.Skip();
            if (this._position == text.Length || text[this._position] != symbol)
            {
                return false;
            }

            this._position++;

            return true;
        }

        public void Expect(char symbol)
        {
            if (!this.Take(symbol))
            {
                throw new InvalidDataException($"Ожидается '{symbol}' около позиции {this._position} SQL-выгрузки.");
            }
        }

        public string Word()
        {
            this.Skip();
            var start = this._position;
            while (this._position < text.Length && (char.IsLetterOrDigit(text[this._position]) || text[this._position] == '_'))
            {
                this._position++;
            }

            if (start == this._position)
            {
                throw new InvalidDataException($"Неподдерживаемый синтаксис SQL около позиции {start}.");
            }

            return text[start..this._position];
        }

        public void ExpectWord(string value)
        {
            if (!this.Word().Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Ожидается {value} в текстовой SQL-выгрузке.");
            }
        }

        public string Identifier()
        {
            this.Skip();
            var value = this._position < text.Length && text[this._position] is '"' or '`'
                ? this.Quoted(text[this._position]) : this.Word();
            if (value.Length is 0 or > 100)
            {
                throw new InvalidDataException("Имя таблицы или столбца должно быть от 1 до 100 символов.");
            }

            return value;
        }

        public string Value()
        {
            this.Skip();
            if (this._position < text.Length && text[this._position] == '\'')
            {
                return this.Quoted('\'');
            }

            var start = this._position;
            while (this._position < text.Length && text[this._position] is not (',' or ')'))
            {
                this._position++;
            }

            var value = text[start..this._position].Trim();
            if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (!Scalar().IsMatch(value))
            {
                throw new InvalidDataException("SQL-значения должны быть строками, числами, NULL или true/false. Выражения и вызовы функций не поддерживаются.");
            }

            return value;
        }

        public IEnumerable<string> CopyLines()
        {
            while (this._position < text.Length && text[this._position] is ' ' or '\t' or '\r')
            {
                this._position++;
            }

            if (this._position >= text.Length || text[this._position++] != '\n')
            {
                throw new InvalidDataException("После COPY ожидаются строки данных.");
            }

            while (this._position < text.Length)
            {
                var end = text.IndexOf('\n', this._position);
                if (end < 0)
                {
                    end = text.Length;
                }

                var line = text[this._position..end].TrimEnd('\r');
                this._position = Math.Min(text.Length, end + 1);
                if (line == "\\.")
                {
                    yield break;
                }

                yield return line;
            }
            throw new InvalidDataException("COPY не завершён строкой \\.");
        }

        private string Quoted(char quote)
        {
            this._position++;
            var value = new StringBuilder();
            while (this._position < text.Length)
            {
                var symbol = text[this._position++];
                if (symbol != quote)
                {
                    value.Append(symbol);
                }
                else if (this._position < text.Length && text[this._position] == quote)
                {
                    value.Append(quote);
                    this._position++;
                }
                else
                {
                    return value.ToString();
                }

                if (value.Length > 10000)
                {
                    throw new InvalidDataException("SQL-ячейка превышает 10000 символов.");
                }
            }
            throw new InvalidDataException("В SQL-выгрузке не закрыта кавычка.");
        }

        private void Skip()
        {
            while (this._position < text.Length)
            {
                if (char.IsWhiteSpace(text[this._position]))
                {
                    this._position++;
                    continue;
                }
                if (text.AsSpan(this._position).StartsWith("--"))
                {
                    var end = text.IndexOf('\n', this._position);
                    this._position = end < 0 ? text.Length : end + 1;
                    continue;
                }
                if (text.AsSpan(this._position).StartsWith("/*"))
                {
                    var end = text.IndexOf("*/", this._position + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw new InvalidDataException("В SQL-выгрузке не закрыт комментарий.");
                    }

                    this._position = end + 2;
                    continue;
                }
                break;
            }
        }
    }

    [GeneratedRegex(@"^(?:[+-]?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Scalar();
}
