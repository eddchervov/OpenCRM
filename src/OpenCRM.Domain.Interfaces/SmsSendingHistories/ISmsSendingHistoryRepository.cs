using System.Linq.Expressions;

namespace OpenCRM.Domain.Interfaces.SmsSendingHistories;

/// <summary> Контракт репозитория истории отправки SMS. </summary>
public interface ISmsSendingHistoryRepository
{
    /// <summary> Проекция запроса по записям истории отправки SMS. </summary>
    IQueryable<T> Get<T>(Expression<Func<SmsSendingHistory, T>> mapExpr) where T : class;

    /// <summary> Считает записи истории отправки SMS. </summary>
    Task<int> Count();

    /// <summary> Добавляет запись истории отправки SMS. </summary>
    Task Add(SmsSendingHistory entity);

    /// <summary> Сохраняет изменения репозитория истории SMS. </summary>
    Task Save();
}

