namespace OpenCRM.Services.Interfaces.SmsSendingHistories.Dtos;

/// <summary> DTO ответа со списком истории отправки SMS. </summary>
public class GetSmsSendingHistoryResponse
{
    /// <summary> Общее количество записей истории. </summary>
    public required int TotalCount { get; set; } 

    /// <summary> Записи истории отправки SMS. </summary>
    public required IEnumerable<GetSmsSendingHistoryDto> Histories { get; set; }
}

