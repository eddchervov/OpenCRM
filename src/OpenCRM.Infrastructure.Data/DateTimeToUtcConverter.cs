using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OpenCRM.Infrastructure.Data;

/// <summary> Преобразователь даты и времени в UTC для EF Core. </summary>
public class DateTimeToUtcConverter : ValueConverter<DateTime, DateTime>
{
    /// <summary> Создаёт конвертер с преобразованием в UTC при записи и чтении. </summary>
    public DateTimeToUtcConverter() : base(
        // При сохранении в БД
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        // При чтении из БД  
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    { }
}

