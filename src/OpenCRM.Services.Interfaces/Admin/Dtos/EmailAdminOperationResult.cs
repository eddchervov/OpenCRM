namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат операции администрирования email-рассылок. </summary>
public sealed class EmailAdminOperationResult<T>
{
    /// <summary> Значение результата операции. </summary>
    public T? Value { get; init; }

    /// <summary> Сообщение об ошибке операции. </summary>
    public string? Error { get; init; }

    /// <summary> Признак успешной операции. </summary>
    public bool Succeeded => this.Error == null;

    /// <summary> Создаёт успешный результат с указанным значением. </summary>
    public static EmailAdminOperationResult<T> Success(T value) => new() { Value = value };

    /// <summary> Создаёт неуспешный результат с указанной ошибкой. </summary>
    public static EmailAdminOperationResult<T> Failure(string error) => new() { Error = error };
}
