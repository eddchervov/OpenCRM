using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Word.Dtos
{
    /// <summary> DTO данных обслуживания для Word-документов. </summary>
    public class WordMaintenanceInfo
    {
        /// <summary> Тип обслуживания в виде числа. </summary>
        public int Type { get; set; }

        /// <summary> Дата обслуживания в текстовом виде. </summary>
        public string Date { get; set; }

        /// <summary> Место проведения обслуживания. </summary>
        public string Location { get; set; }

        /// <summary> Исполнитель обслуживания. </summary>
        public string Performer { get; set; }

        /// <summary> Пробег на момент обслуживания, если известен. </summary>
        public int? Mileage { get; set; }

        /// <summary> Комментарий к обслуживанию. </summary>
        public string Comment { get; set; }

        /// <summary> Стоимость обслуживания, если указана. </summary>
        public decimal? Price { get; set; }

        /// <summary> Код валюты денежных сумм обслуживания. </summary>
        public string CurrencyCode { get; set; } = "RUB";

        /// <summary> Строки работ и услуг обслуживания. </summary>
        public List<WordMaintenanceDetail> MaintenanceDetails { get; set; }

        /// <summary> Фотографии, относящиеся к обслуживанию. </summary>
        public IEnumerable<WordPhotoInfo> Photos { get; set; }
    }
}

