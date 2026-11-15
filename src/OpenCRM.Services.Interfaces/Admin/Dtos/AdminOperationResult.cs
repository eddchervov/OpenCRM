namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат административной операции. </summary>
public sealed class AdminOperationResult
{
    /// <summary> Признак успешного выполнения. </summary>
    public bool Succeeded { get; init; }

    /// <summary> Признак отсутствия записи. </summary>
    public bool NotFound { get; init; }

    /// <summary> Текст ошибки. </summary>
    public string? ErrorMessage { get; init; }

    /// <summary> Идентификатор затронутой записи. </summary>
    public int? Id { get; init; }

    /// <summary> Сообщение результата. </summary>
    public string? Message { get; init; }

    /// <summary> Создаёт результат успешной операции. </summary>
    public static AdminOperationResult Success(string message, int? id = null)
    {
        return new AdminOperationResult
        {
            Succeeded = true,
            Id = id,
            Message = message
        };
    }

    /// <summary> Создаёт результат отсутствия записи. </summary>
    public static AdminOperationResult Missing(string errorMessage)
    {
        return new AdminOperationResult
        {
            NotFound = true,
            ErrorMessage = errorMessage
        };
    }

    /// <summary> Создаёт результат некорректного запроса. </summary>
    public static AdminOperationResult Invalid(string errorMessage)
    {
        return new AdminOperationResult
        {
            ErrorMessage = errorMessage
        };
    }
}
