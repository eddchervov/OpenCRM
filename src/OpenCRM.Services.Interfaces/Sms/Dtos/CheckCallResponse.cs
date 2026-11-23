using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Sms.Dtos;

/// <summary> DTO ответа на проверку звонка SMS-сервиса. </summary>
public class CheckCallResponse
{
    /// <summary> Статус проверки звонка. </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary> Числовой статус проверки звонка. </summary>
    public int Check_status { get; set; }
}
