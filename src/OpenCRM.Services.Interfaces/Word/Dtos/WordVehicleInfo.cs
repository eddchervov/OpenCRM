using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Word.Dtos
{
    /// <summary> Данные транспортного средства для экспорта в документ Word. </summary>
    public class WordVehicleInfo
    {
        /// <summary> Наименование марки транспортного средства. </summary>
        public string BrandName { get; set; }

        /// <summary> Наименование модели транспортного средства. </summary>
        public string ModelName { get; set; }

        /// <summary> Регистрационный знак транспортного средства. </summary>
        public string? RegistrationPlate { get; set; }

        /// <summary> Единица измерения пробега транспортного средства. </summary>
        public VehicleMileageUnit MileageUnit { get; set; } = VehicleMileageUnit.Kilometers;

        /// <summary> Год выпуска транспортного средства. </summary>
        public int? YearIssue { get; set; }

        /// <summary> Название транспортного средства. </summary>
        public string Name { get; set; }

        /// <summary> Описание транспортного средства. </summary>
        public string Description { get; set; }

        /// <summary> Двоичные данные фотографии транспортного средства. </summary>
        public byte[] PhotoData { get; set; }
    }

}


