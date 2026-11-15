using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces.SmsSendingHistories;
using System.Linq.Expressions;

namespace OpenCRM.Infrastructure.Data.Repositories;

/// <summary> Репозиторий истории отправки SMS. </summary>
public class SmsSendingHistoryRepository(OpenCRMContext opencrmContext) : ISmsSendingHistoryRepository
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает проекцию запроса по истории отправки SMS. </summary>
    public IQueryable<T> Get<T>(Expression<Func<SmsSendingHistory, T>> mapExpr) where T : class
    {
        return _opencrmContext.SmsSendingHistories.Select(mapExpr);
    }

    /// <summary> Возвращает количество записей истории отправки SMS. </summary>
    public async Task<int> Count()
    {
        return await _opencrmContext.SmsSendingHistories.CountAsync();
    }

    /// <summary> Добавляет запись истории отправки SMS. </summary>
    public async Task Add(SmsSendingHistory entity)
    {
        await _opencrmContext.SmsSendingHistories.AddAsync(entity);
    }

    /// <summary> Сохраняет изменения в базе данных. </summary>
    public async Task Save()
    {
        await _opencrmContext.SaveChangesAsync();
    }
}
