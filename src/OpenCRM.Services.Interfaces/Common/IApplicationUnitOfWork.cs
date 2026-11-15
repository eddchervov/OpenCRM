namespace OpenCRM.Services.Interfaces.Common;

/// <summary> Выполнение операций в единой транзакции. </summary>
public interface IApplicationUnitOfWork
{
    /// <summary> Выполняет действие внутри транзакции. </summary>
    Task ExecuteInTransactionAsync(Func<Task> action);
}
