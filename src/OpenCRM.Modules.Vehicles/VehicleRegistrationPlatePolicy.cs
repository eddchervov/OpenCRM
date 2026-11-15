using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Проверяет маски номеров из конфигурации и нормализует буквенные аналоги. </summary>
public sealed class VehicleRegistrationPlatePolicy : IVehicleRegistrationPlatePolicy
{
    private const int MinimumLength = 2;
    private const int MaximumPatternLength = 256;
    private readonly TimeSpan _regexTimeout;
    private readonly int _maxLength;
    private readonly int _maxSearchVariants;
    private readonly string _letterPattern;
    private readonly Regex _letterRegex;
    private readonly IReadOnlyDictionary<string, string> _characterMappings;
    private readonly IReadOnlyList<VehicleRegistrationPlateFormat> _formats;

    /// <summary> Загружает и проверяет форматы регистрационных номеров из конфигурации. </summary>
    public VehicleRegistrationPlatePolicy(IConfiguration configuration)
    {
        var section = configuration.GetSection("VehicleRegistrationPlates");
        if (!int.TryParse(section["MaxLength"], NumberStyles.None, CultureInfo.InvariantCulture, out this._maxLength) ||
            this._maxLength is < MinimumLength or > 64)
        {
            throw new InvalidOperationException("VehicleRegistrationPlates:MaxLength должен быть от 2 до 64.");
        }

        this._letterPattern = section["LetterPattern"]?.Trim() ?? string.Empty;
        if (this._letterPattern.Length is 0 or > 64)
        {
            throw new InvalidOperationException("Не задан короткий шаблон буквы VehicleRegistrationPlates:LetterPattern.");
        }

        if (!int.TryParse(section["RegexTimeoutMilliseconds"], NumberStyles.None, CultureInfo.InvariantCulture, out var regexTimeoutMilliseconds) ||
            regexTimeoutMilliseconds is < 1 or > 5000)
        {
            throw new InvalidOperationException("VehicleRegistrationPlates:RegexTimeoutMilliseconds должен быть от 1 до 5000.");
        }

        this._regexTimeout = TimeSpan.FromMilliseconds(regexTimeoutMilliseconds);

        if (!int.TryParse(section["MaxSearchVariants"], NumberStyles.None, CultureInfo.InvariantCulture, out this._maxSearchVariants) ||
            this._maxSearchVariants is < 1 or > 256)
        {
            throw new InvalidOperationException("VehicleRegistrationPlates:MaxSearchVariants должен быть от 1 до 256.");
        }

        try
        {
            this._letterRegex = this.CompileRegex($"^(?:{this._letterPattern})$");
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Некорректный VehicleRegistrationPlates:LetterPattern.", exception);
        }

        var characterMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mapping in section.GetSection("CharacterMappings").GetChildren())
        {
            var key = mapping.Key.ToUpperInvariant();
            var value = mapping.Value;
            if (key.Length != 1 || key[0] is < 'A' or > 'Z' || !this.IsSingleCyrillicUppercase(value))
            {
                throw new InvalidOperationException("Таблица CharacterMappings должна сопоставлять одиночные латинские буквы кириллическим.");
            }

            if (!characterMappings.TryAdd(key, value!))
            {
                throw new InvalidOperationException("В CharacterMappings повторяется латинская буква.");
            }
        }

        if (characterMappings.Count == 0)
        {
            throw new InvalidOperationException("Не задана таблица VehicleRegistrationPlates:CharacterMappings.");
        }

        this._characterMappings = new ReadOnlyDictionary<string, string>(characterMappings);
        this._formats = this.ReadFormats(section.GetSection("Formats"));
        if (this._formats.Count == 0)
        {
            throw new InvalidOperationException("Не заданы форматы VehicleRegistrationPlates:Formats.");
        }
    }

    /// <summary> Нормализует номер; соответствие формату проверяется отдельно методом Validate. </summary>
    public string? Normalize(string? value, bool allowOtherFormat = false)
    {
        if (allowOtherFormat)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return this.NormalizeSafe(value);
    }

    /// <summary> Возвращает true, когда номер пустой или подходит формату указанного вида техники. </summary>
    public bool MatchesFormat(string? value, VehicleKind kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var formats = this.FormatsFor(kind);
        if (formats.Count == 0)
        {
            return true;
        }

        try
        {
            var normalized = this.NormalizeSafe(value);

            return normalized is null || this.MatchesStandardFormat(normalized, formats);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary> Проверяет допустимые символы, длину и, когда он задан, шаблон вида техники. </summary>
    public void Validate(string? value, VehicleKind kind, bool allowOtherFormat = false)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidOperationException("Выберите поддерживаемый тип техники.");
        }

        if (allowOtherFormat)
        {
            _ = this.NormalizeSafe(value);

            return;
        }

        var normalized = this.NormalizeSafe(value);
        if (normalized is null)
        {
            return;
        }

        var formats = this.FormatsFor(kind);
        if (formats.Count > 0 && !this.MatchesStandardFormat(normalized, formats))
        {
            throw new InvalidOperationException("Госномер не соответствует формату выбранного вида техники.");
        }
    }

    /// <summary> Возвращает настройки формы без серверных regex-объектов. </summary>
    public VehicleRegistrationPlateFormatsDto GetFormats() => new()
    {
        MaxLength = this._maxLength,
        LetterPattern = this._letterPattern,
        CharacterMappings = this._characterMappings,
        Formats = this._formats.Select(format => new VehicleRegistrationPlateFormatDto
        {
            Code = format.Code,
            Kinds = format.Kinds.Select(kind => (int)kind).ToArray(),
            Masks = format.Masks,
            Pattern = format.Pattern,
            Example = format.Example
        }).ToArray()
    };

    /// <summary> Возвращает ограниченный набор вариантов частичного номера без проверки длины и формата. </summary>
    public IReadOnlyList<string> GetSearchVariants(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var characterVariants = new List<IReadOnlyList<string>>();
        var normalized = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || rune.Value == '-')
            {
                continue;
            }

            var key = Rune.ToUpperInvariant(rune).ToString();
            var canonical = this._characterMappings.TryGetValue(key, out var mapped) ? mapped : key;
            normalized.Append(canonical);

            var variants = new List<string> { canonical };
            foreach (var mapping in this._characterMappings)
            {
                if (string.Equals(mapping.Value, canonical, StringComparison.Ordinal))
                {
                    variants.Add(mapping.Key);
                }
            }

            characterVariants.Add(variants.Distinct(StringComparer.Ordinal).ToArray());
        }

        var results = new List<string>(this._maxSearchVariants) { value };
        VehicleRegistrationPlatePolicy.AddSearchVariant(results, normalized.ToString(), this._maxSearchVariants);
        var combinations = new List<string> { string.Empty };
        foreach (var variants in characterVariants)
        {
            var nextCombinations = new List<string>();
            foreach (var combination in combinations)
            {
                foreach (var variant in variants)
                {
                    VehicleRegistrationPlatePolicy.AddSearchVariant(nextCombinations, combination + variant, this._maxSearchVariants);
                    if (nextCombinations.Count == this._maxSearchVariants)
                    {
                        break;
                    }
                }

                if (nextCombinations.Count == this._maxSearchVariants)
                {
                    break;
                }
            }

            combinations = nextCombinations;
        }

        foreach (var combination in combinations)
        {
            VehicleRegistrationPlatePolicy.AddSearchVariant(results, combination, this._maxSearchVariants);
        }

        return results;
    }

    private bool MatchesStandardFormat(string normalized, IReadOnlyList<VehicleRegistrationPlateFormat> formats)
    {
        if (normalized.EnumerateRunes().Any(character => Rune.IsLetter(character) && !this._letterRegex.IsMatch(character.ToString())))
        {
            return false;
        }

        return formats.Any(format => format.Matches(normalized));
    }

    private IReadOnlyList<VehicleRegistrationPlateFormat> ReadFormats(IConfigurationSection section)
    {
        var formats = new List<VehicleRegistrationPlateFormat>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignedKinds = new HashSet<VehicleKind>();
        foreach (var item in section.GetChildren())
        {
            var code = item["Code"]?.Trim();
            var pattern = item["Pattern"]?.Trim();
            var example = item["Example"]?.Trim();
            var kinds = item.GetSection("Kinds").GetChildren().Select(kind =>
            {
                if (!int.TryParse(kind.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || !Enum.IsDefined(typeof(VehicleKind), value))
                {
                    throw new InvalidOperationException($"Формат {code ?? "(без кода)"} содержит неизвестный вид техники.");
                }

                return (VehicleKind)value;
            }).Distinct().ToArray();
            var masks = item.GetSection("Masks").GetChildren().Select(mask => mask.Value?.Trim() ?? string.Empty).ToArray();
            if (string.IsNullOrWhiteSpace(code) || code.Length > 40 || !codes.Add(code) || kinds.Length == 0 || masks.Length == 0 ||
                string.IsNullOrWhiteSpace(pattern) || pattern.Length > MaximumPatternLength || string.IsNullOrWhiteSpace(example))
            {
                throw new InvalidOperationException("Каждый формат должен иметь уникальный код, виды техники, маски, шаблон и пример.");
            }

            if (kinds.Any(kind => !assignedKinds.Add(kind)))
            {
                throw new InvalidOperationException($"Вид техники повторяется в форматах регистрационных номеров {code}.");
            }

            Regex patternRegex;
            try
            {
                patternRegex = this.CompileRegex(pattern);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"Некорректный шаблон регистрационного номера {code}.", exception);
            }

            var maskRegexes = masks.Select(mask => this.CompileMask(code, mask)).ToArray();
            var safeExample = this.NormalizeSafe(example);
            if (safeExample is null || safeExample.Length > this._maxLength || !patternRegex.IsMatch(safeExample) ||
                !maskRegexes.Any(mask => mask.IsMatch(safeExample)))
            {
                throw new InvalidOperationException($"Пример регистрационного номера {code} не совпадает с его шаблонами.");
            }

            formats.Add(new VehicleRegistrationPlateFormat(code, kinds, masks, pattern, example, patternRegex, maskRegexes));
        }

        return formats.AsReadOnly();
    }

    private Regex CompileMask(string code, string mask)
    {
        if (string.IsNullOrWhiteSpace(mask) || mask.Length > this._maxLength || mask.Any(character => character is not ('L' or '#')))
        {
            throw new InvalidOperationException($"Маска формата {code} должна содержать только L и #.");
        }

        var pattern = new StringBuilder("^");
        foreach (var character in mask)
        {
            pattern.Append(character == 'L' ? $"(?:{this._letterPattern})" : "[0-9]");
        }

        pattern.Append('$');

        try
        {
            return this.CompileRegex(pattern.ToString());
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"Некорректная маска формата {code}.", exception);
        }
    }

    private Regex CompileRegex(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, this._regexTimeout);

    private IReadOnlyList<VehicleRegistrationPlateFormat> FormatsFor(VehicleKind kind) =>
        this._formats.Where(format => format.Kinds.Contains(kind)).ToArray();

    private string? NormalizeSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > this._maxLength)
        {
            throw new InvalidOperationException($"Госномер не должен быть длиннее {this._maxLength} символов.");
        }

        var normalized = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                throw new InvalidOperationException("Госномер не может содержать управляющие символы.");
            }

            if (!Rune.IsLetterOrDigit(rune) && !Rune.IsWhiteSpace(rune) && rune.Value != '-')
            {
                throw new InvalidOperationException("Госномер может содержать буквы, цифры, пробелы и дефисы.");
            }

            if (Rune.IsWhiteSpace(rune) || rune.Value == '-')
            {
                continue;
            }

            var key = Rune.ToUpperInvariant(rune).ToString();
            normalized.Append(this._characterMappings.TryGetValue(key, out var replacement) ? replacement : key);
        }

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("Госномер должен содержать буквы или цифры.");
        }

        if (normalized.Length < MinimumLength)
        {
            throw new InvalidOperationException($"Госномер должен содержать не менее {MinimumLength} букв или цифр.");
        }

        return normalized.ToString();
    }

    private bool IsSingleCyrillicUppercase(string? value) => value is { Length: 1 } &&
        char.IsUpper(value[0]) &&
        (value[0] == 'Ё' || value[0] is >= 'А' and <= 'Я');

    private static void AddSearchVariant(List<string> variants, string value, int maximumCount)
    {
        if (variants.Count >= maximumCount || value.Length == 0 || variants.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        variants.Add(value);
    }

    private sealed record VehicleRegistrationPlateFormat(
        string Code,
        IReadOnlyList<VehicleKind> Kinds,
        IReadOnlyList<string> Masks,
        string Pattern,
        string Example,
        Regex Regex,
        IReadOnlyList<Regex> MaskRegexes)
    {
        public bool Matches(string value) =>
            this.Regex.IsMatch(value) && this.MaskRegexes.Any(mask => mask.IsMatch(value));
    }
}
